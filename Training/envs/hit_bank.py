"""Shoves drawn from real hits (train_box.py --hit-bank PATH; off by default).

The bank is a .npz written by tools/hits_to_bank.py from the game's fight log: one row a landed punch, with the part it
landed on (0 head, 1 body), its direction in the taker's heading frame (x towards the puncher's side, so a straight
punch pushes along -x) and its impulse, N s (the game's PUNCH_MASS x closing speed). In training each fighter is hit
about once every `every_s` seconds by a row drawn at random: a force of impulse / dt for one control step, on the trunk
at the head's or the torso's centre (the head is a shape on the trunk body), which is how the game's MjRing pushes a
boxer that is hit (hit_push). `level` (a 0-dim tensor, 0 to 1) scales every impulse: the trainer ramps it from 0 to 1
for a curriculum (--hit-ramp-iters).

The force is added to xfrc_applied just before the physics and taken off just after, so whatever else a stage writes
there (footwork's shoves, the get-up's helping hand, the punches' hit_push) is left as it was.
"""

from __future__ import annotations

import mujoco
import numpy as np
import torch
import warp as wp

from .math3d import quat_yaw

KEYS = ("part", "dir", "impulse")


def load_bank(path: str) -> dict:
    with np.load(path, allow_pickle=False) as z:
        missing = [k for k in KEYS if k not in z.files]
        if missing:
            raise ValueError(f"{path}: not a hit bank (missing {missing}); write one with tools/hits_to_bank.py")
        bank = {k: np.asarray(z[k]) for k in KEYS}
    n = bank["impulse"].shape[0]
    if n == 0:
        raise ValueError(f"{path}: the hit bank is empty")
    if bank["dir"].shape != (n, 3) or bank["part"].shape != (n,):
        raise ValueError(f"{path}: the hit bank's arrays disagree in length")
    return bank


class HitShover:
    def __init__(self, env, path: str, every_s: float = 4.0, scale: float = 1.0):
        self.env = env
        dev = env.device
        bank = load_bank(path)
        self.part = torch.tensor(bank["part"], device=dev, dtype=torch.long).clamp(0, 1)
        d = torch.tensor(bank["dir"], device=dev, dtype=torch.float32)
        self.dir = d / d.norm(dim=-1, keepdim=True).clamp_min(1e-6)
        self.impulse = torch.tensor(bank["impulse"], device=dev, dtype=torch.float32).clamp_min(0.0)
        self.M = int(self.impulse.shape[0])
        self.every_s, self.scale = float(every_s), float(scale)
        self.level = torch.ones((), device=dev)
        self.xfrc = wp.to_torch(env.dw.xfrc_applied)
        self.xipos = wp.to_torch(env.dw.xipos)
        self.torso = [mujoco.mj_name2id(env.m, mujoco.mjtObj.mjOBJ_BODY, p + "torso") for p in ("a_", "b_")[: env.K]]
        self.torso_t = torch.tensor(self.torso, device=dev, dtype=torch.long)  # a list index would copy from the CPU
        self.cur = torch.zeros(env.N, env.K, 6, device=dev)
        self.count = torch.zeros((), device=dev)
        self.impulse_sum = torch.zeros((), device=dev)

    def apply(self) -> None:
        """Draw this control step's hits and add their force and torque to the trunks."""
        e = self.env
        N, K = e.N, e.K
        start = torch.rand(N, K, generator=e.rng, device=e.device) < e.dt / self.every_s
        pick = torch.randint(0, self.M, (N, K), generator=e.rng, device=e.device)
        j = self.impulse[pick] * self.scale * self.level * start.float()  # (N, K) N s
        yaw = quat_yaw(e.xquat[:, e.pelvis])  # (N, K)
        loc = self.dir[pick]
        c, s = torch.cos(yaw), torch.sin(yaw)
        world = torch.stack([c * loc[..., 0] - s * loc[..., 1], s * loc[..., 0] + c * loc[..., 1], loc[..., 2]], -1)
        f = world * (j / e.dt)[..., None]
        head, body = e.geom_xpos[:, e.g_head], e.geom_xpos[:, e.g_torso]  # (N, K, 3)
        at = torch.where((self.part[pick] == 0)[..., None], head, body)
        arm = at - self.xipos[:, self.torso_t]
        self.cur.copy_(torch.cat([f, torch.cross(arm, f, dim=-1)], -1))  # in place, for envs/step_graph.py
        for k, b in enumerate(self.torso):
            self.xfrc[:, b] += self.cur[:, k]
        self.count += start.float().sum()
        self.impulse_sum += j.sum()

    def remove(self) -> None:
        for k, b in enumerate(self.torso):
            self.xfrc[:, b] -= self.cur[:, k]
