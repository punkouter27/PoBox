"""Free-for-all C MuJoCo exam (F5, FreeForAll.cs rules in a N-boxer solid ring).

Usage:
    PYTHONPATH=.mj350 python tools/ffa_exam.py --bouts 20 --rounds 3 --json logs/ffa_exam.json
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time

import mujoco
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "tools"))

import rig_to_mjcf as R  # noqa: E402
import exam as E  # noqa: E402
import exam_v2 as V  # noqa: E402
from eval_cmujoco import heading, yaw_of  # noqa: E402
from footwork_c import speed_limit  # noqa: E402

NAMES = ("matt", "zombie", "nick", "sokol", "kim", "trump", "grandma", "grandpa")


def load_arena(names: list, models_dir: str, rigs_dir: str):
    fighters, cfgs, bodies, qkeys = [], [], [], []
    r = R.RING_HALF - 0.5
    n = len(names)
    for idx, name in enumerate(names):
        cfg = json.load(open(os.path.join(models_dir, f"{name}_policy_config.json"), encoding="utf-8"))
        rig = json.load(open(os.path.join(rigs_dir, f"{name}.json"), encoding="utf-8"))
        prefix = f"f{idx}_"
        f = R.Fighter(rig, prefix, float(cfg.get("stance_lean_deg", 0.0)))
        ang = idx * 2.0 * math.pi / n
        c, s = math.cos(ang), math.sin(ang)
        m = max(abs(c), abs(s), 1e-6)
        x, y = (c / m) * r, (s / m) * r
        yaw = math.atan2(-y, -x)
        bodies.append(f.build((x, y), yaw))
        qkeys.append(R.key_for(f, x, y, yaw, float(cfg["stand_height"])))
        fighters.append(f)
        cfgs.append(cfg)
    xml = R.assemble("ffa_ring", fighters, bodies, R.ring_xml(), " ".join(qkeys))
    model = mujoco.MjModel.from_xml_string(xml)
    return model, cfgs


class FfaRing:
    """Multi-boxer C MuJoCo ring matching FreeForAll.cs target switching, walk-out, count, and punch rules."""

    def __init__(self, names: list, models_dir: str, rigs_dir: str, seed: int = 1):
        self.names = names
        self.K = len(names)
        self.m, self.cfgs = load_arena(names, models_dir, rigs_dir)
        self.d = mujoco.MjData(self.m)
        self.rng = np.random.default_rng(seed)
        self.ring_half = R.RING_HALF
        self.dt = float(self.m.opt.timestep) * 4

        m = self.m
        base = m.dof_damping.copy()
        for k, cfg in enumerate(self.cfgs):
            speed_limit(m, f"f{k}_", cfg)
        self.damp_base = base
        self.sl_extra = m.dof_damping - base
        self.getup_share = [float(c.get("getup_speed_share", 1.0)) for c in self.cfgs]

        J, G, B = mujoco.mjtObj.mjOBJ_JOINT, mujoco.mjtObj.mjOBJ_GEOM, mujoco.mjtObj.mjOBJ_BODY
        self.order = self.cfgs[0]["joint_order"]
        self.A = len(self.order)
        self.rq, self.rv, self.jq, self.jv = [], [], [], []
        self.pelvis, self.torso_b, self.head, self.torso, self.glove, self.forearm, self.foot = [], [], [], [], [], [], []
        self.f_lo, self.f_hi, self.scale_j = [], [], []

        for k, cfg in enumerate(self.cfgs):
            p = f"f{k}_"
            r = mujoco.mj_name2id(m, J, p + "root")
            self.rq.append(int(m.jnt_qposadr[r]))
            self.rv.append(int(m.jnt_dofadr[r]))
            js = [mujoco.mj_name2id(m, J, p + n) for n in self.order]
            self.jq.append(np.array([m.jnt_qposadr[j] for j in js]))
            self.jv.append(np.array([m.jnt_dofadr[j] for j in js]))
            self.pelvis.append(mujoco.mj_name2id(m, B, p + "pelvis"))
            self.torso_b.append(mujoco.mj_name2id(m, B, p + "torso"))
            self.head.append(mujoco.mj_name2id(m, G, p + "head_geom"))
            self.torso.append(mujoco.mj_name2id(m, G, p + "torso_geom"))
            self.glove.append([mujoco.mj_name2id(m, G, p + f"glove_{s}") for s in "lr"])
            self.forearm.append([mujoco.mj_name2id(m, G, p + f"forearm_{s}_geom") for s in "lr"])
            self.foot.append([mujoco.mj_name2id(m, G, p + f"foot_{s}_geom") for s in "lr"])
            self.scale_j.append(np.array(cfg.get("action_scale_j", [0.5] * self.A), dtype=float))
            if cfg.get("force_lo") and cfg.get("force_hi"):
                self.f_lo.append(np.array(cfg["force_lo"], dtype=float))
                self.f_hi.append(np.array(cfg["force_hi"], dtype=float))
            else:
                fl = np.array(cfg["force_limit"], dtype=float)
                self.f_lo.append(-fl)
                self.f_hi.append(fl)

        self.kp = np.array([m.actuator_gainprm[k * self.A:(k + 1) * self.A, 0] for k in range(self.K)])
        self.kv = np.array([-m.actuator_biasprm[k * self.A:(k + 1) * self.A, 2] for k in range(self.K)])
        self.key = m.key_qpos[0].copy()
        self.default = [self.key[self.jq[k]].copy() for k in range(self.K)]
        self.stand = [float(cfg["stand_height"]) for cfg in self.cfgs]
        self.lo = [np.array(c["lower"]) for c in self.cfgs]
        self.hi = [np.array(c["upper"]) for c in self.cfgs]
        self.r_glove = [float(m.geom_size[self.glove[k][0]][0]) for k in range(self.K)]
        self.r_head = [float(m.geom_size[self.head[k]][0]) for k in range(self.K)]
        self.r_torso = [float(m.geom_size[self.torso[k]][0]) for k in range(self.K)]
        self.half = [float(m.geom_size[self.torso[k]][1]) for k in range(self.K)]
        self.r_arm = [float(m.geom_size[self.forearm[k][0]][0]) for k in range(self.K)]
        self.arm_half = [float(m.geom_size[self.forearm[k][0]][1]) for k in range(self.K)]

        rule = lambda key: np.array([float(c.get(key, 0.0)) for c in self.cfgs])
        self.hit_push, self.stagger_v, self.hurt_full, self.body_drain = (
            rule("hit_push"), rule("stagger_v"), rule("hurt_full"), rule("body_drain")
        )
        self.ceiling = [bool(c.get("fatigue_ceiling", False)) for c in self.cfgs]
        self.hurt = np.zeros(self.K)
        # Stand-in guard offsets (head, torso, glove_l, glove_r) in each fighter's pelvis-heading frame.
        mujoco.mj_resetDataKeyframe(m, self.d, 0)
        mujoco.mj_forward(m, self.d)
        self.guard = []
        for k in range(self.K):
            pos_k = self.d.xpos[self.pelvis[k]]
            y_k = yaw_of(self.d.xquat[self.pelvis[k]])
            pts = [self.d.geom_xpos[self.head[k]], self.d.geom_xpos[self.torso[k]],
                   self.d.geom_xpos[self.glove[k][0]], self.d.geom_xpos[self.glove[k][1]]]
            self.guard.append([heading(p - pos_k, y_k) for p in pts])
        self.reset_round(keep_hurt=False)

    def reset_round(self, keep_hurt: bool = True) -> None:
        m, d = self.m, self.d
        mujoco.mj_resetData(m, d)
        q = self.key.copy()
        # Randomise ring slot order each round (FreeForAll.ToTheRopes).
        order = self.rng.permutation(self.K)
        r = self.ring_half - 0.5
        for k, slot in enumerate(order):
            ang = slot * 2.0 * math.pi / self.K + self.rng.uniform(-0.08, 0.08)
            c, s = math.cos(ang), math.sin(ang)
            mx = max(abs(c), abs(s), 1e-6)
            x, y = (c / mx) * r, (s / mx) * r
            yaw = math.atan2(-y, -x)
            rq = self.rq[k]
            q[rq:rq + 2] = [x, y]
            q[rq + 2] = self.stand[k] + 0.002
            q[rq + 3:rq + 7] = [math.cos(yaw / 2), 0.0, 0.0, math.sin(yaw / 2)]
            q[self.jq[k]] = self.default[k]
        d.qpos[:] = q
        d.qvel[:] = 0.0
        d.ctrl[:] = np.concatenate([q[self.jq[k]] for k in range(self.K)])
        mujoco.mj_forward(m, d)

        self.last = np.zeros((self.K, self.A))
        self.target = np.full(self.K, -1, dtype=int)
        self.prev_head = np.array([d.geom_xpos[self.head[k]].copy() for k in range(self.K)])
        self.head_vel = np.zeros((self.K, 3))
        self.push = np.zeros((self.K, 6))
        self.scale = np.ones(self.K)
        self.reserve = np.ones(self.K)
        self.stagger = np.zeros(self.K)
        self.daze = np.zeros(self.K)
        self.legs_out = np.zeros(self.K)
        if not keep_hurt:
            self.hurt = np.zeros(self.K)
        else:
            self.hurt *= 0.85
        self.out = np.zeros(self.K, dtype=bool)
        self.down = np.zeros(self.K, dtype=bool)
        self.down_timer = np.zeros(self.K)
        self.stood_for = np.zeros(self.K)
        self.walking = np.ones(self.K, dtype=bool)
        self.walk_age = np.zeros(self.K)
        self.since_hit_by = np.full((self.K, self.K), 99.0)
        self.blend_at = np.zeros(self.K, dtype=int)
        self.held = [None] * self.K
        self.no_drain = np.zeros(self.K, dtype=bool)
        # Punch detection state per attacker k, glove i, against current opponent o
        self.armed = np.ones((self.K, 2), dtype=bool)
        self.cool = np.zeros((self.K, 2))
        self.prev_closing = np.zeros((self.K, 2, 2))
        self.prev_glove = np.array([[d.geom_xpos[self.glove[k][i]].copy() for i in range(2)] for k in range(self.K)])
        self.prev_tb = np.array([d.geom_xpos[self.torso[k]].copy() for k in range(self.K)])

    def posture(self, k: int):
        p = self.d.xpos[self.pelvis[k]]
        up = self.d.xmat[self.pelvis[k]].reshape(3, 3)[2, 2]
        return p[2] / self.stand[k], float(up)

    def pick_target(self, k: int) -> int:
        pos = self.d.xpos[self.pelvis[k]][:2]
        on = np.zeros(self.K, dtype=int)
        for j in range(self.K):
            if j != k and not self.down[j] and not self.out[j] and self.target[j] >= 0:
                on[self.target[j]] += 1
        cur = self.target[k]
        best, best_score = -1, 1e9
        cur_score = 1e9
        for o in range(self.K):
            if o == k or self.out[o] or self.down[o]:
                continue
            d = float(np.linalg.norm(self.d.xpos[self.pelvis[o]][:2] - pos))
            grudge = 1.0 if self.since_hit_by[k, o] < 3.0 else 0.0
            crowd = max(0, on[o] - 1) * 1.0
            daze_share = min(1.0, self.daze[o] / 42.0) * 0.5
            score = d - grudge + crowd - daze_share
            if o == cur:
                cur_score = score
            if score < best_score:
                best, best_score = o, score
        if best < 0:
            # All others are down: face closest remaining body just to keep observation finite.
            for o in range(self.K):
                if o == k:
                    continue
                d = float(np.linalg.norm(self.d.xpos[self.pelvis[o]][:2] - pos))
                if d < best_score:
                    best, best_score = o, d
            return max(0, best)
        if cur >= 0 and cur != best and not self.out[cur] and not self.down[cur]:
            if cur_score - best_score < 0.4:
                return cur
        return best

    def observe_100(self, k: int, opp: int, stand_in: bool = False) -> np.ndarray:
        d = self.d
        pos = d.xpos[self.pelvis[k]]
        R = d.xmat[self.pelvis[k]].reshape(3, 3)
        yaw = yaw_of(d.xquat[self.pelvis[k]])
        rv = self.rv[k]
        lin_w = d.qvel[rv:rv + 3]
        foot = []
        for g in self.foot[k]:
            sole = d.geom_xpos[g][2] - np.abs(d.geom_xmat[g].reshape(3, 3)[2]) @ self.m.geom_size[g]
            foot.append(1.0 if sole < 0.005 else 0.0)
        rel = lambda p: heading(p - pos, yaw)
        if stand_in:
            # Stand-in 1.2 m in front, facing fighter k (exact match to footwork_c.py / ExamRing).
            opp_xy = pos[:2] + np.array([math.cos(yaw), math.sin(yaw)]) * 1.2
            theirs = math.atan2(pos[1] - opp_xy[1], pos[0] - opp_xy[0])
            c, s = math.cos(theirs), math.sin(theirs)
            place = lambda gp: np.array([opp_xy[0] + c * gp[0] - s * gp[1], opp_xy[1] + s * gp[0] + c * gp[1], self.stand[k] + gp[2]])
            th, tb, g_l, g_r = (place(gp) for gp in self.guard[k])
            tvel = -lin_w
            ogloves = np.concatenate([rel(g_l), rel(g_r)])
            dyaw = theirs - yaw
        else:
            th = d.geom_xpos[self.head[opp]]
            tb = d.geom_xpos[self.torso[opp]]
            tvel = (self.head_vel[opp] - lin_w)
            ogloves = np.concatenate([rel(d.geom_xpos[self.glove[opp][0]]), rel(d.geom_xpos[self.glove[opp][1]])])
            dyaw = yaw_of(d.xquat[self.pelvis[opp]]) - yaw
        ring = heading(np.array([-pos[0], -pos[1], 0.0]), yaw)[:2] / self.ring_half
        return np.concatenate([
            R.T @ lin_w, d.qvel[rv + 3:rv + 6], R.T @ np.array([0.0, 0.0, -1.0]),
            d.qpos[self.jq[k]] - self.default[k], d.qvel[self.jv[k]], self.last[k],
            foot, [pos[2]],
            rel(th), rel(tb), heading(tvel, yaw),
            rel(d.geom_xpos[self.glove[k][0]]), rel(d.geom_xpos[self.glove[k][1]]),
            ogloves, [math.cos(dyaw), math.sin(dyaw)], ring,
        ]).clip(-100.0, 100.0)

    def _brain_cmd(self, pol, obs100: np.ndarray, k: int, cmd3: np.ndarray, is_match: bool) -> np.ndarray:
        n = pol.obs_rms.mean.shape[0]
        if is_match and getattr(pol, "obs_state", False):
            share = min(1.0, self.daze[k] / 42.0)
            opp = max(0, self.target[k])
            oshare = min(1.0, self.daze[opp] / 42.0)
            tail = np.array([self.reserve[k], share, oshare], dtype=obs100.dtype)
        else:
            tail = cmd3.astype(obs100.dtype)
        if n > 103:
            tail = np.concatenate([tail, np.zeros(n - 103, dtype=obs100.dtype)])
        return E.act(pol, np.concatenate([obs100, tail]))

    def step(self, pols: dict, round_age: float):
        d, dt = self.d, self.dt
        for k in range(self.K):
            if self.out[k] or self.down[k]:
                continue
            self.target[k] = self.pick_target(k)
            if self.walking[k]:
                opp = self.target[k]
                odist = float(np.linalg.norm(d.xpos[self.pelvis[opp]][:2] - d.xpos[self.pelvis[k]][:2])) if opp >= 0 else 9.0
                cdist = float(np.linalg.norm(d.xpos[self.pelvis[k]][:2]))
                if odist < 1.6 or cdist < 0.8 or self.walk_age[k] >= 6.0:
                    self.walking[k] = False

        acts = np.zeros((self.K, self.A))
        for k, name in enumerate(self.names):
            opp = max(0, self.target[k])
            if self.out[k]:
                continue
            if self.down[k]:
                obs = self.observe_100(k, opp, stand_in=True)
                acts[k] = self._brain_cmd(pols[name]["getup"], obs, k, np.zeros(3), False)
                self.no_drain[k] = True
            elif self.walking[k]:
                obs = self.observe_100(k, opp, stand_in=True)
                p = d.xpos[self.pelvis[k]][:2]
                yaw = yaw_of(d.xquat[self.pelvis[k]])
                to_mid = -p
                dist = float(np.linalg.norm(to_mid))
                vy_w = to_mid / max(dist, 1e-4) * 0.6
                v_loc = heading(np.array([vy_w[0], vy_w[1], 0.0]), yaw)
                ang_err = math.atan2(to_mid[1], to_mid[0]) - yaw
                ang_err = (ang_err + math.pi) % (2.0 * math.pi) - math.pi
                wz = float(np.clip(2.0 * ang_err, -1.5, 1.5))
                acts[k] = self._brain_cmd(pols[name]["footwork"], obs, k, np.array([v_loc[0], v_loc[1], wz]), False)
                self.walk_age[k] += dt
                self.no_drain[k] = False
            else:
                obs = self.observe_100(k, opp, stand_in=False)
                acts[k] = self._brain_cmd(pols[name]["match"], obs, k, np.zeros(3), True)
                self.no_drain[k] = False

        # Apply speed limit per fighter (getup uses getup_speed_share), drive targets, fatigue ceiling
        for k in range(self.K):
            jv = self.jv[k]
            share = self.getup_share[k] if self.down[k] else 1.0
            self.m.dof_damping[jv] = self.damp_base[jv] + share * self.sl_extra[jv]
            if self.out[k]:
                qpos_k = d.qpos[self.jq[k]]
                d.ctrl[k * self.A:(k + 1) * self.A] = qpos_k
                continue
            act_k = np.clip(acts[k], -3.0, 3.0)
            brain_key = "getup" if self.down[k] else ("footwork" if self.walking[k] else "match")
            af = float(self.cfgs[k].get(f"rules_{brain_key}", {}).get("action_filter", 0.0))
            if af > 0.0:
                act_k = af * self.last[k] + (1.0 - af) * act_k
            self.last[k] = act_k
            want = np.clip(self.default[k] + act_k * self.scale_j[k], self.lo[k], self.hi[k])
            if self.blend_at[k] > 0 and self.held[k] is not None:
                tot = max(1, int(self.cfgs[k].get("rules_match", {}).get("blend_steps", 0)))
                w = max(0.0, self.blend_at[k] / tot)
                want = w * self.held[k] + (1.0 - w) * want
                self.blend_at[k] -= 1
            q = d.qpos[self.jq[k]]
            tired = 1.0 - 0.4 * min(1.0, max(0.0, 1.0 - self.reserve[k] / 0.5))
            if self.down[k]:
                want = q + 0.04 * (want - q) if self.down_timer[k] < 0.8 else want
            elif self.ceiling[k] and tired < 0.999:
                want = q + self.scale[k] * (want - q)
                base = q + self.kv[k] * d.qvel[jv] / self.kp[k]
                want = np.clip(want, base + tired * self.f_lo[k] / self.kp[k], base + tired * self.f_hi[k] / self.kp[k])
            elif self.scale[k] < 0.999 or (not self.ceiling[k] and tired < 0.999):
                scale = self.scale[k] * (1.0 if self.ceiling[k] else tired)
                want = q + scale * (want - q)
            d.ctrl[k * self.A:(k + 1) * self.A] = want
            d.xfrc_applied[self.torso_b[k]] = self.push[k]

        mujoco.mj_step(self.m, d, 4)
        self.push[:] = 0.0

        # Head velocities, reserve drain, stagger decay
        for k in range(self.K):
            joules = float(self.cfgs[k].get("fatigue_j", 0.0)) * float(self.cfgs[k].get("strength", 1.0))
            if joules > 0.0 and not self.no_drain[k] and not self.out[k]:
                spent = np.abs(d.actuator_force[k * self.A:(k + 1) * self.A] * d.qvel[self.jv[k]]).sum() / joules
                self.reserve[k] = float(np.clip(self.reserve[k] + dt * ((1.0 - self.reserve[k]) / 20.0 - spent), 0.0, 1.0))
            head = d.geom_xpos[self.head[k]].copy()
            self.head_vel[k] = (head - self.prev_head[k]) / dt
            self.prev_head[k] = head
            self.since_hit_by[k] += dt
            if self.stagger[k] > 0.0:
                self.stagger[k] = max(0.0, self.stagger[k] - dt)
                self.scale[k] = 0.55 if self.stagger[k] > 0.0 else 1.0

        # Punches against every standing opponent
        hits = [0] * self.K
        hit_taken = [False] * self.K
        cur_glove = np.array([[d.geom_xpos[self.glove[k][i]].copy() for i in range(2)] for k in range(self.K)])
        cur_tb = np.array([d.geom_xpos[self.torso[k]].copy() for k in range(self.K)])
        for k in range(self.K):
            if self.out[k] or self.down[k]:
                self.prev_glove[k] = cur_glove[k]
                continue
            for i in range(2):
                self.cool[k, i] = max(0.0, self.cool[k, i] - dt)
                g = cur_glove[k, i]
                vg = (g - self.prev_glove[k, i]) / dt
                self.prev_glove[k, i] = g
                # Check closest opponent (either target or any standing body hit accidentally)
                best_o, best_gap, best_zone, best_spd, best_n, best_reach = -1, 1e9, 1, 0.0, None, 0.0
                for o in range(self.K):
                    if o == k or self.out[o] or self.down[o]:
                        continue
                    th, tb = d.geom_xpos[self.head[o]], cur_tb[o]
                    vh, vb = self.head_vel[o], (tb - self.prev_tb[o]) / dt
                    ax = d.geom_xmat[self.torso[o]].reshape(3, 3)[:, 2]
                    to_h = th - g
                    dh = float(np.linalg.norm(to_h))
                    along = float(np.clip((g - tb) @ ax, -self.half[o], self.half[o]))
                    to_b = tb + ax * along - g
                    db = float(np.linalg.norm(to_b))
                    gap = np.array([dh - self.r_glove[k] - self.r_head[o], db - self.r_glove[k] - self.r_torso[o]])
                    z = int(gap.argmin())
                    if gap[z] < best_gap:
                        nh, nb = to_h / max(dh, 1e-4), to_b / max(db, 1e-4)
                        closing = np.array([min((vg - vh) @ nh, vg @ nh), min((vg - vb) @ nb, vg @ nb)])
                        best_o, best_gap, best_zone = o, float(gap[z]), z
                        best_spd = float(np.clip(closing[z], 0.0, 9.0))
                        best_n = nh if z == 0 else nb
                        best_reach = (dh - self.r_head[o]) if z == 0 else (db - self.r_torso[o])
                if best_o >= 0 and best_gap < 0.012 and self.armed[k, i] and best_spd > 1.0 and self.cool[k, i] <= 0.0:
                    self.armed[k, i] = False
                    self.cool[k, i] = 0.25
                    hits[k] += 1
                    hit_taken[best_o] = True
                    self.since_hit_by[best_o, k] = 0.0
                    # Daze / push on taker
                    daze_add = best_spd * (1.0 if best_zone == 0 else 0.3)
                    self.daze[best_o] = self.daze[best_o] * math.exp(-dt / 2.5) + daze_add
                    if self.hurt_full[best_o] > 0.0:
                        self.hurt[best_o] += daze_add
                    if self.hit_push[best_o] > 0.0:
                        j = best_spd * 2.2 * self.hit_push[best_o]
                        f = best_n * (j / dt)
                        arm = (cur_glove[k, i] + best_n * best_reach) - d.xipos[self.torso_b[best_o]]
                        self.push[best_o] = np.concatenate([f, np.cross(arm, f)])
                    if self.stagger_v[best_o] > 0.0 and best_spd >= self.stagger_v[best_o]:
                        self.stagger[best_o] = 0.3
                        self.scale[best_o] = 0.55
                elif best_gap > 0.30:
                    self.armed[k, i] = True
        self.prev_tb[:] = cur_tb
        return hits, hit_taken


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--models", default=os.path.join(HERE, "models", "v3"))
    ap.add_argument("--rigs", default=os.path.join(HERE, "rigs", "v3"))
    ap.add_argument("--bouts", type=int, default=20)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--round-s", type=float, default=45.0)
    ap.add_argument("--boxers", nargs="+", default=list(NAMES))
    ap.add_argument("--book", default="", help="optional policies JSON; defaults to checkpoints/onnx/*")
    ap.add_argument("--json", default="")
    args = ap.parse_args()

    t0 = time.time()
    names = args.boxers
    ring = FfaRing(names, args.models, args.rigs)
    book = json.load(open(args.book, encoding="utf-8")) if args.book else {}
    pols = {}
    for name in names:
        b = book.get(name, {})
        f_p = os.path.join(HERE, b.get("footwork", f"checkpoints/onnx/footwork_{name}.pt"))
        m_p = os.path.join(HERE, b.get("match", f"checkpoints/onnx/match_{name}.pt"))
        g_p = os.path.join(HERE, b.get("getup", f"checkpoints/onnx/getup_{name}.pt"))
        pols[name] = {
            "footwork": V.load_policy(f_p, ring.A),
            "match": V.load_policy(m_p, ring.A),
            "getup": V.load_policy(g_p, ring.A),
        }

    stats = {n: dict(stand_s=0.0, slips=0, knockdowns=0, beat_count=0, hits=0, kos=0) for n in names}
    steps_per_round = int(args.round_s / ring.dt)
    for bout in range(args.bouts):
        ring.hurt[:] = 0.0
        for rnd in range(args.rounds):
            ring.reset_round(keep_hurt=(rnd > 0))
            low = np.zeros(ring.K)
            since_hit = np.full(ring.K, 99.0)
            for s in range(steps_per_round):
                hits, taken = ring.step(pols, s * ring.dt)
                for k, n in enumerate(names):
                    stats[n]["hits"] += hits[k]
                    if taken[k]:
                        since_hit[k] = 0.0
                    else:
                        since_hit[k] += ring.dt
                    if ring.out[k]:
                        continue
                    h, up = ring.posture(k)
                    if not ring.down[k]:
                        stats[n]["stand_s"] += ring.dt
                        low[k] = low[k] + ring.dt if (h < 0.6 or up < 0.4) else 0.0
                        if low[k] > 0.4:
                            ring.down[k] = True
                            ring.down_timer[k] = 0.0
                            ring.stood_for[k] = 0.0
                            stats[n]["knockdowns"] += 1
                            if since_hit[k] >= 1.5:
                                stats[n]["slips"] += 1
                    else:
                        ring.down_timer[k] += ring.dt
                        if h >= 0.8 and up >= 0.85:
                            ring.stood_for[k] += ring.dt
                            if ring.stood_for[k] >= 1.5:
                                ring.down[k] = False
                                ring.held[k] = ring.d.qpos[ring.jq[k]].copy()
                                ring.blend_at[k] = int(ring.cfgs[k].get("rules_match", {}).get("blend_steps", 0))
                                stats[n]["beat_count"] += 1
                        else:
                            ring.stood_for[k] = 0.0
                            if ring.down_timer[k] >= 10.0:
                                ring.down[k] = False
                                ring.out[k] = True
                                stats[n]["kos"] += 1
                if int((~ring.out).sum()) <= 1:
                    break
        print(f"  bout {bout + 1}/{args.bouts} ({time.time() - t0:.0f}s)", flush=True)

    tot_s = sum(v["stand_s"] for v in stats.values())
    tot_slips = sum(v["slips"] for v in stats.values())
    tot_kds = sum(v["knockdowns"] for v in stats.values())
    tot_up = sum(v["beat_count"] for v in stats.values())
    tot_hits = sum(v["hits"] for v in stats.values())
    per = {}
    for n, v in stats.items():
        mins = max(1e-6, v["stand_s"] / 60.0)
        per[n] = {
            "slips_per_min": round(v["slips"] / mins, 3),
            "hits_per_min": round(v["hits"] / mins, 2),
            "knockdowns": v["knockdowns"],
            "beat_count": v["beat_count"],
            "getup_rate": round(v["beat_count"] / max(1, v["knockdowns"]), 3),
            "kos": v["kos"],
        }
    out = {
        "bouts": args.bouts, "rounds": args.rounds, "boxers": names, "elapsed_s": round(time.time() - t0, 1),
        "slips_per_boxer_min": round(tot_slips / max(1e-6, tot_s / 60.0), 3),
        "hits_per_boxer_min": round(tot_hits / max(1e-6, tot_s / 60.0), 2),
        "knockdowns": tot_kds, "getups_beaten_count": tot_up,
        "getup_beat_count_rate": round(tot_up / max(1, tot_kds), 3),
        "per_boxer": per,
    }
    print(json.dumps({k: v for k, v in out.items() if k != "per_boxer"}, indent=2))
    for n, v in per.items():
        print(f"  {n:8s} slips={v['slips_per_min']:.2f}/m  hits={v['hits_per_min']:.1f}/m  up={v['beat_count']}/{v['knockdowns']} ({v['getup_rate']:.0%})  ko={v['kos']}")
    if args.json:
        os.makedirs(os.path.dirname(os.path.abspath(args.json)), exist_ok=True)
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(out, fh, indent=2)


if __name__ == "__main__":
    main()
