"""Adversarial motion priors (Peng et al. 2021): a discriminator over transitions (s, s'), and its style reward.

Opt-in (train_box.py --amp); without it nothing here runs and style.py's judge (--style-w) is the footwork stage's
style reward, as before. The policy never sees a clip and gets no new input: the exported ONNX is the same either way.

    discriminator   an MLP over (phi(s), phi(s')), trained with the least-squares GAN loss (a clip's transition 1,
                    the policy's -1) and a gradient penalty on the clips' transitions
    style reward    r = max(0, 1 - 0.25 (D - 1)^2), added to the task reward times --amp-w
    replay buffer   the policy's transitions of the last few iterations (a ring buffer on the device); the
                    discriminator takes one step per PPO minibatch, inside PPO.update (ppo.py, PPO.set_amp)

phi(s), one fighter (lengths in leg lengths, so one clip file serves every body):
    root height (1), which way is down in the root frame (3), root velocity in the heading frame (3),
    root angular velocity (3), joint angles and speeds of a subset of the joints (2 J), end-effector positions in the
    root frame (3 E).
The subset is the feature set: "legs" (abdomen, hips, knees, ankles; feet) for the walking clips, whose arms hang where
a boxer's are in the guard, and "full" (every joint; feet and gloves) for getting up, where the arms do the work.

Clips are the CMU files tools/retarget_clips.py writes: clips/walk_turn.npz (walking and turning; in the repo) and
clips/getup.npz (getting up off the floor; NOT in the repo, see `python amp.py prep-getup`).

    python amp.py check                     the env reads a clip's state as the clip's features; the discriminator learns
    python amp.py prep-getup [--fetch]      retarget CMU get-up takes into clips/getup.npz
"""

from __future__ import annotations

import json
import os
import sys
from typing import Dict, Sequence

import mujoco
import numpy as np
import torch

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "tools"))
from envs.math3d import quat_rotate_inverse, quat_yaw, to_heading  # noqa: E402
from ppo import mlp  # noqa: E402

CLIPS_DIR = os.path.join(HERE, "clips")
WALK_CLIPS = os.path.join(CLIPS_DIR, "walk_turn.npz")
GETUP_CLIPS = os.path.join(CLIPS_DIR, "getup.npz")
LEG_PARTS = ("abdomen", "hip", "knee", "ankle")
FEATURE_SETS = {
    # joints by their name's first word (None = all), end effectors by geom name (after the fighter's prefix)
    "legs": (LEG_PARTS, ("foot_l_geom", "foot_r_geom")),
    "full": (None, ("foot_l_geom", "foot_r_geom", "glove_l", "glove_r")),
}
# CMU subject 140, "getting up from ground" (mocap.cs.cmu.edu). Retargeted 2026-11-25: 01-04, 08 and 09 rise from a
# pelvis of 0.24-0.51 m to 0.83-0.85 m; 05 is 0.2 s long, and 06 (29.7 s) and 07 start and end standing, so they are
# left out (06 alone would be a third of the clip frames, mostly standing).
GETUP_TAKES = {f"140_{i:02d}": "get up from the ground" for i in (1, 2, 3, 4, 8, 9)}
NON_CLIP_KEYS = ("joint_order", "fps", "descriptions", "ground")


# ---- the features ---------------------------------------------------------------------------------------------
def joint_subset(order: Sequence[str], feature_set: str) -> list[int]:
    parts = FEATURE_SETS[feature_set][0]
    return [i for i, n in enumerate(order) if parts is None or n.split("_")[0] in parts]


def frame_dim(n_joints: int, n_ee: int) -> int:
    return 1 + 3 + 3 + 3 + 2 * n_joints + 3 * n_ee


def frame_features(pos, quat, lin_w, ang_b, jp, jv, ee_w, leg) -> torch.Tensor:
    """phi(s) for a batch: pos (B,3), quat (B,4) wxyz, lin_w (B,3) world, ang_b (B,3) body, jp/jv (B,J), ee_w (B,E,3)
    world, leg (B,1). The one function both the env and the clips go through."""
    down = torch.tensor([0.0, 0.0, -1.0], device=pos.device, dtype=pos.dtype).expand_as(pos)
    grav_b = quat_rotate_inverse(quat, down)
    lin_h = to_heading(lin_w, quat_yaw(quat)) / leg
    ee = quat_rotate_inverse(quat[:, None].expand(-1, ee_w.shape[1], -1), ee_w - pos[:, None]) / leg[..., None]
    return torch.cat([pos[:, 2:3] / leg, grav_b, lin_h, ang_b, jp, jv, ee.flatten(1)], -1)


def leg_length(m: mujoco.MjModel, prefix: str) -> float:
    B = mujoco.mjtObj.mjOBJ_BODY
    return float(sum(np.linalg.norm(m.body_pos[mujoco.mj_name2id(m, B, prefix + n)]) for n in ("shin_l", "foot_l")))


class EnvReader:
    """phi(s) of every fighter of a BoxingEnv (or any of its stages), read straight off its MuJoCo Warp state:
    (N * K, frame_dim). Nothing in the env changes."""

    def __init__(self, env, feature_set: str):
        m, dev, K = env.m, env.device, env.K
        order = env.cfg["joint_order"]
        self.env = env
        self.idx = torch.tensor(joint_subset(order, feature_set), device=dev, dtype=torch.long)
        G = mujoco.mjtObj.mjOBJ_GEOM
        prefixes = ("a_", "b_")[:K]
        self.ee = torch.tensor(
            [[mujoco.mj_name2id(m, G, p + n) for n in FEATURE_SETS[feature_set][1]] for p in prefixes],
            device=dev,
            dtype=torch.long,
        )  # (K, E)
        self.leg = torch.tensor([leg_length(m, p) for p in prefixes], device=dev)
        self.dim = frame_dim(len(self.idx), self.ee.shape[1])

    def __call__(self) -> torch.Tensor:
        e, N, K = self.env, self.env.N, self.env.K
        pos, quat = e.xpos[:, e.pelvis], e.xquat[:, e.pelvis]  # (N, K, ...)
        lin_w = torch.stack([e.qvel[:, rv : rv + 3] for rv in e.root_v], 1)
        ang_b = torch.stack([e.qvel[:, rv + 3 : rv + 6] for rv in e.root_v], 1)
        jp, jv = e.qpos[:, e.jq][..., self.idx], e.qvel[:, e.jv][..., self.idx]
        ee = e.geom_xpos[:, self.ee]  # (N, K, E, 3)
        leg = self.leg.view(1, K, 1).expand(N, K, 1)
        f = lambda x: x.reshape(N * K, *x.shape[2:])
        return frame_features(f(pos), f(quat), f(lin_w), f(ang_b), f(jp), f(jv), f(ee), f(leg))


def place_lowest(m: mujoco.MjModel, clip: np.ndarray, order: list, prefix: str = "a_") -> np.ndarray:
    """A clip on a body, scaled by its legs, with the lowest point of any of its geoms on the floor in every frame (for
    get-up clips, where the body lies on the canvas; retarget_clips.place puts the lower sole there, right for a walk)."""
    d = mujoco.MjData(m)
    B, Jn = mujoco.mjtObj.mjOBJ_BODY, mujoco.mjtObj.mjOBJ_JOINT
    root = int(m.jnt_qposadr[mujoco.mj_name2id(m, Jn, prefix + "root")])
    adr = [int(m.jnt_qposadr[mujoco.mj_name2id(m, Jn, prefix + n)]) for n in order]
    pelvis = mujoco.mj_name2id(m, B, prefix + "pelvis")
    geoms = [g for g in range(m.ngeom) if m.geom_bodyid[g] > 0 and m.body_rootid[m.geom_bodyid[g]] == pelvis]
    T = mujoco.mjtGeom

    def bottom(g: int) -> float:
        z, R, s, t = d.geom_xpos[g][2], d.geom_xmat[g].reshape(3, 3), m.geom_size[g], m.geom_type[g]
        if t == T.mjGEOM_SPHERE:
            return z - s[0]
        if t in (T.mjGEOM_CAPSULE, T.mjGEOM_CYLINDER):
            return z - abs(R[2, 2]) * s[1] - s[0] * (1.0 if t == T.mjGEOM_CAPSULE else np.hypot(R[2, 0], R[2, 1]))
        return z - np.abs(R[2]) @ s  # box (and an ellipsoid's bounding box)

    out = clip.astype(np.float64).copy()
    out[:, :3] *= leg_length(m, prefix)
    for i, row in enumerate(out):
        d.qpos[root : root + 7], d.qpos[adr] = row[:7], row[7:]
        mujoco.mj_kinematics(m, d)
        out[i, 2] -= min(bottom(g) for g in geoms)
    return out


def load_clips(path: str) -> tuple[dict, float, str]:
    z = np.load(path)
    ground = str(z["ground"]) if "ground" in z.files else "feet"
    return {n: z[n] for n in z.files if n not in NON_CLIP_KEYS}, float(z["fps"]), ground


def clip_frames(m: mujoco.MjModel, order: list, path: str, feature_set: str, prefix: str = "a_", dt: float = 0.0):
    """phi of every frame of every clip on this body, and the (qpos, qvel) each came from.
    Returns (frames (rows, D) float32, starts: the index of each clip's first row, states). A clip whose rate is not
    1/dt is resampled to it (dt 0: as recorded)."""
    from retarget_clips import place

    clips, fps, ground = load_clips(path)
    J, G = mujoco.mjtObj.mjOBJ_JOINT, mujoco.mjtObj.mjOBJ_GEOM
    root_q = int(m.jnt_qposadr[mujoco.mj_name2id(m, J, prefix + "root")])
    root_v = int(m.jnt_dofadr[mujoco.mj_name2id(m, J, prefix + "root")])
    jq = np.array([m.jnt_qposadr[mujoco.mj_name2id(m, J, prefix + n)] for n in order])
    jv = np.array([m.jnt_dofadr[mujoco.mj_name2id(m, J, prefix + n)] for n in order])
    idx = joint_subset(order, feature_set)
    ee = [mujoco.mj_name2id(m, G, prefix + n) for n in FEATURE_SETS[feature_set][1]]
    pelvis = mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_BODY, prefix + "pelvis")
    leg = leg_length(m, prefix)
    d = mujoco.MjData(m)
    step = dt if dt > 0.0 else 1.0 / fps
    cols = {k: [] for k in ("pos", "quat", "lin", "ang", "jp", "jv", "ee")}
    starts, states, n = [], [], 0
    for name, clip in clips.items():
        rows = (place_lowest if ground == "any" else place)(m, clip, order, prefix)
        if dt > 0.0 and abs(dt * fps - 1.0) > 1e-6:
            src = np.arange(len(rows)) / fps
            dst = np.arange(0.0, src[-1], dt)
            rows = np.stack([np.interp(dst, src, rows[:, c]) for c in range(rows.shape[1])], 1)
            rows[:, 3:7] /= np.linalg.norm(rows[:, 3:7], axis=1, keepdims=True)
        if len(rows) < 3:
            continue
        starts.append(n)
        q0, q1, v = m.qpos0.copy(), m.qpos0.copy(), np.zeros(m.nv)
        for a, b in zip(rows[:-1], rows[1:]):
            q0[root_q : root_q + 7], q0[jq] = a[:7], a[7:]
            q1[root_q : root_q + 7], q1[jq] = b[:7], b[7:]
            mujoco.mj_differentiatePos(m, v, step, q0, q1)
            d.qpos[:] = q0
            mujoco.mj_kinematics(m, d)
            cols["pos"].append(d.xpos[pelvis].copy())
            cols["quat"].append(d.xquat[pelvis].copy())
            cols["lin"].append(v[root_v : root_v + 3].copy())
            cols["ang"].append(v[root_v + 3 : root_v + 6].copy())
            cols["jp"].append(q0[jq][idx])
            cols["jv"].append(v[jv][idx])
            cols["ee"].append(d.geom_xpos[ee].copy())
            states.append((q0.copy(), v.copy()))
            n += 1
    if not n:
        raise SystemExit(f"{path}: no clip long enough")
    t = {k: torch.tensor(np.array(v), dtype=torch.float32) for k, v in cols.items()}
    frames = frame_features(t["pos"], t["quat"], t["lin"], t["ang"], t["jp"], t["jv"], t["ee"], torch.full((n, 1), leg))
    return frames.numpy(), starts, states


def clip_transitions(frames: np.ndarray, starts: Sequence[int]) -> np.ndarray:
    """(phi_t, phi_t+1) of every pair of consecutive frames within a clip: (pairs, 2 D)."""
    ends = list(starts[1:]) + [len(frames)]
    pairs = [np.concatenate([frames[s : e - 1], frames[s + 1 : e]], 1) for s, e in zip(starts, ends) if e - s >= 2]
    return np.concatenate(pairs, 0).astype(np.float32)


# ---- the discriminator ----------------------------------------------------------------------------------------
class AMP:
    """The discriminator, its replay buffer of the policy's transitions, and the style reward.

    expert: (rows, 2 D) clip transitions. The inputs are scaled by the clips' own mean and spread, fixed (the gradient
    penalty is taken in those scaled numbers, so a feature that hardly varies cannot be all of it, as style.py found).
    """

    def __init__(
        self,
        expert: np.ndarray,
        device: str,
        hidden=(1024, 512),
        lr: float = 1e-4,
        gp: float = 5.0,
        buffer: int = 200_000,
        batch: int = 4096,
        logit_reg: float = 0.01,
    ):
        self.device = device
        self.expert = torch.tensor(expert, device=device, dtype=torch.float32)
        self.mean, self.std = self.expert.mean(0), self.expert.std(0) + 1e-3
        self.expert_n = self._n(self.expert)
        self.net = mlp(expert.shape[1], tuple(hidden), 1).to(device)
        self.opt = torch.optim.Adam(self.net.parameters(), lr=lr)
        self.gp, self.batch, self.logit_reg = gp, batch, logit_reg
        self.buf = torch.zeros(buffer, expert.shape[1], device=device)
        self.buf_n = 0  # rows filled (host count: the buffer only grows, then wraps)
        self.buf_at = 0
        # Its own random stream: attached to PPO, the discriminator's draws leave the policy update's unchanged.
        self.gen = torch.Generator(device=device)
        self.gen.manual_seed(0)

    def _n(self, x: torch.Tensor) -> torch.Tensor:
        return ((x - self.mean) / self.std).clamp(-10.0, 10.0)

    def logits(self, x: torch.Tensor) -> torch.Tensor:
        return self.net(self._n(x)).squeeze(-1)

    @torch.no_grad()
    def reward(self, trans: torch.Tensor) -> torch.Tensor:
        """1 for a transition the discriminator takes for a clip's, 0 for one it is sure is the policy's."""
        return (1.0 - 0.25 * (self.logits(trans) - 1.0) ** 2).clamp_min(0.0)

    @torch.no_grad()
    def observe(self, trans: torch.Tensor) -> None:
        """The policy's transitions (already masked to the ones that count) into the replay buffer."""
        trans = trans[-len(self.buf) :]
        n, cap = len(trans), len(self.buf)
        first = min(n, cap - self.buf_at)
        self.buf[self.buf_at : self.buf_at + first] = trans[:first]
        if n > first:
            self.buf[: n - first] = trans[first:]
        self.buf_at = (self.buf_at + n) % cap
        self.buf_n = min(cap, self.buf_n + n)

    def step(self) -> Dict[str, float]:
        """One discriminator update on a minibatch of clip and replayed policy transitions (no host sync)."""
        if self.buf_n == 0:
            return {}
        dev, B = self.device, self.batch
        pick = torch.randint(0, len(self.expert_n), (B,), device=dev, generator=self.gen)
        real = self.expert_n[pick].requires_grad_(True)
        fake = self._n(self.buf[torch.randint(0, self.buf_n, (B,), device=dev, generator=self.gen)])
        d_r, d_f = self.net(real).squeeze(-1), self.net(fake).squeeze(-1)
        slope = torch.autograd.grad(d_r.sum(), real, create_graph=True)[0]
        gp = (slope**2).sum(-1).mean()
        loss = ((d_r - 1.0) ** 2).mean() + ((d_f + 1.0) ** 2).mean() + self.gp * gp
        if self.logit_reg > 0.0:
            loss = loss + self.logit_reg * self.net[-1].weight.pow(2).sum()
        self.opt.zero_grad(set_to_none=True)
        loss.backward()
        self.opt.step()
        return {"amp_loss": loss.detach(), "amp_gp": gp.detach(), "amp_d_clips": d_r.mean().detach(),
                "amp_d_policy": d_f.mean().detach()}  # fmt: skip

    def state_dict(self) -> dict:
        return {"net": self.net.state_dict(), "opt": self.opt.state_dict()}

    def load_state_dict(self, sd: dict) -> None:
        self.net.load_state_dict(sd["net"])
        self.opt.load_state_dict(sd["opt"])


class AMPReward:
    """What a trainer holds: reads phi before and after each env step, pays the style reward, feeds the buffer.

    amp = AMPReward.for_env(env, stage, device, ...)
    obs = env.reset(); amp.reset()
    obs, rew, done, timeout = env.step(act)
    rew = rew + amp.pay(done)          # amp_w * style reward on the transitions that count, 0 elsewhere
    """

    def __init__(self, env, disc: AMP, reader: EnvReader, weight: float, stage: str):
        self.env, self.disc, self.reader, self.w, self.stage = env, disc, reader, weight, stage
        self.prev = None
        self.paid = torch.zeros((), device=env.device)
        self.steps = 0

    @classmethod
    def for_env(cls, env, stage: str, device: str, weight: float, clips: str = "", feature_set: str = "auto", **kw):
        getup = stage == "getup"
        clips = clips or (GETUP_CLIPS if getup else WALK_CLIPS)
        if not os.path.exists(clips):
            raise SystemExit(
                f"[amp] {os.path.relpath(clips, HERE)} is missing."
                + (" Make it with `python amp.py prep-getup --fetch` (CMU subject 140)." if getup else "")
            )
        if feature_set == "auto":
            feature_set = "full" if getup else "legs"
        reader = EnvReader(env, feature_set)
        frames, starts, _ = clip_frames(env.m, env.cfg["joint_order"], clips, feature_set, dt=env.dt)
        disc = AMP(clip_transitions(frames, starts), device, **kw)
        return cls(env, disc, reader, weight, stage)

    def reset(self) -> None:
        self.prev = self.reader()

    def mask(self) -> torch.Tensor:
        """Which fighters' transitions are judged (N * K,): a footwork walk or turn (the clips' kind of moment); in the
        get-up stage, a fighter not yet up (the clips end standing, the stage then asks for the guard); else all."""
        e = self.env
        if hasattr(e, "style_mask"):
            return e.style_mask()
        if self.stage == "getup":
            h = e.xpos[:, e.pelvis][..., 2]  # (N, K)
            return (h < 0.9 * e.stand_height.view(1, -1)).reshape(-1)
        return torch.ones(e.N * e.K, dtype=torch.bool, device=e.device)

    def pay(self, done: torch.Tensor) -> torch.Tensor:
        """Call right after env.step: the reward to add, (N * K,). A transition that ended an episode is not judged:
        the state after it is the next episode's first."""
        nxt = self.reader()
        trans = torch.cat([self.prev, nxt], -1)
        keep = self.mask() & ~done.bool()
        r = self.w * self.disc.reward(trans) * keep.float()
        self.disc.observe(trans[keep])
        self.prev = nxt
        self.paid += r.mean()
        self.steps += 1
        return r

    def stats(self) -> Dict[str, float]:
        out = {"rt_amp": self.paid.item() / max(1, self.steps), "amp_buffer": float(self.disc.buf_n)}
        self.paid.zero_()
        self.steps = 0
        return out


# ---- the get-up clips -----------------------------------------------------------------------------------------
def prep_getup(models: str, body: str, fetch: bool, out: str = GETUP_CLIPS) -> None:
    """Retarget the CMU get-up takes (clips/cmu/140_XX.bvh) onto the boxers' 21 joints, into clips/getup.npz (same rows
    as walk_turn.npz; `ground` = "any": placed with the lowest geom on the floor, not the lower sole)."""
    import urllib.request

    from retarget_clips import URL, retarget

    cfg = json.load(open(os.path.join(models, f"{body}_policy_config.json"), encoding="utf-8"))
    order, lower, upper = cfg["joint_order"], np.array(cfg["lower"]), np.array(cfg["upper"])
    m = mujoco.MjModel.from_xml_path(os.path.join(models, f"{body}_solo.xml"))
    cache = os.path.join(CLIPS_DIR, "cmu")
    os.makedirs(cache, exist_ok=True)
    clips, missing = {}, []
    for name, what in GETUP_TAKES.items():
        path = os.path.join(cache, name + ".bvh")
        if not os.path.exists(path):
            if not fetch:
                missing.append(name)
                continue
            urllib.request.urlretrieve(URL.format(subject=int(name.split("_")[0]), name=name), path)
        clip, _, clipped = retarget(path, order, lower, upper)
        rows = place_lowest(m, clip, order)
        print(f"{name}: {len(clip) / 50:5.1f} s, {clipped:5.1%} of angles clipped to the joint ranges, "
              f"pelvis {rows[0, 2]:.2f} m -> {rows[-1, 2]:.2f} m   {what}")  # fmt: skip
        clips[name] = clip
    if missing:
        print(f"not in {os.path.relpath(cache, HERE)}: {', '.join(m_ + '.bvh' for m_ in missing)} (--fetch gets them)")
    if not clips:
        raise SystemExit("no get-up clips: nothing written")
    np.savez_compressed(
        out, joint_order=np.array(order), fps=50, descriptions=json.dumps(GETUP_TAKES), ground="any", **clips
    )
    print(f"wrote {out}: {len(clips)} clips")


def check() -> None:
    """The env, put in a clip's state, reads the clip's features; and the discriminator learns to tell clips from noise."""
    from envs.footwork import FootworkEnv

    xml = os.path.join(HERE, "models", "v2", "matt_solo.xml")
    env = FootworkEnv(xml, 4, device="cpu", seed=1, obs_noise=0.0, disturb=0.0, randomise=0.0, cuda_graph=False)
    order = env.cfg["joint_order"]
    frames, starts, states = clip_frames(env.m, order, WALK_CLIPS, "legs", dt=env.dt)
    reader = EnvReader(env, "legs")
    worst = 0.0
    for i in range(0, len(states), max(1, len(states) // 8))[:8]:
        q, v = states[i]
        env.qpos[0] = torch.tensor(q, dtype=torch.float32)
        env.qvel[0] = torch.tensor(v, dtype=torch.float32)
        env._physics_forward()
        worst = max(worst, float((reader()[0] - torch.tensor(frames[i])).abs().max()))
    print(f"{len(frames)} frames; the env's reading of 8 of them differs by at most {worst:.1e}")


if __name__ == "__main__":
    import argparse

    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("check")
    p = sub.add_parser("prep-getup")
    p.add_argument("--models", default=os.path.join(HERE, "models", "v2"))
    p.add_argument("--body", default="matt")
    p.add_argument("--fetch", action="store_true", help="download missing takes from the CMU BVH mirror (small files)")
    a = ap.parse_args()
    if a.cmd == "check":
        check()
    else:
        prep_getup(a.models, a.body, a.fetch)
