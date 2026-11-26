"""Terrain adaptation for the footwork rung (train_box.py --terrain; classes in config/progression.json).

Each episode a world draws a floor class (flat, mild, steep by default): a slope the gravity is tilted by (the same
physics as a sloped floor seen from the floor, as envs/footwork.py's --slope-deg has it), a contact-friction factor, and
a rate of uneven-ground impulses. The class is re-drawn at every reset, so a policy meets all of them inside one run
instead of being trained on one floor and hoping. The observation's "which way is down" is the tilted gravity, so a body
feels the slope exactly as it would feel it standing on one.

Nothing here changes the game: like the domain randomisation it only makes a brain robust to the floor it is put on.
"""

from __future__ import annotations

import math

import numpy as np
import torch
import warp as wp

from . import limits


def _grow(model, name: str, n: int):
    """A model field copied out to one row a world if it is held once (MuJoCo Warp indexes it by world)."""
    field = getattr(model, name)
    if field.shape[0] != n:
        host = np.repeat(field.numpy(), n, 0)
        setattr(model, name, wp.array(host, dtype=field.dtype, device=field.device))
    return wp.to_torch(getattr(model, name))


class Terrain:
    """env.terrain: the floor of every world, re-drawn at every reset; bump() is the uneven ground under a foot."""

    def __init__(self, env, r: limits.TerrainRanges):
        self.env, self.r = env, r
        N, dev = env.N, env.device
        self.classes = r.classes
        self.weights = torch.tensor([c.weight for c in self.classes], device=dev)
        self.cum = torch.cumsum(self.weights / self.weights.sum(), 0)
        self.mag = float(np.linalg.norm(env.m.opt.gravity))
        if env.mw.opt.gravity.shape[0] != N:
            env.mw.opt.gravity = wp.array(
                np.repeat(env.mw.opt.gravity.numpy(), N, 0), dtype=wp.vec3, device=env.mw.opt.gravity.device
            )
        self.gvec = wp.to_torch(env.mw.opt.gravity)  # (N,3), written in place every reset
        # The observation's "down" is one array for the whole run (the physical limits keep writing into it, in place).
        gw = env.gravity_world
        down0 = gw.reshape(-1, 3)[0] if gw.dim() > 1 else gw
        down0 = (down0 / down0.norm().clamp_min(1e-6)).to(dev)
        env.gravity_world = down0.view(1, 1, 3).expand(N, 1, 3).clone()
        self.gw = env.gravity_world
        self.scalevec = torch.ones(N, device=dev)  # times g, set by envs/constraints.py when gravity is scaled
        self.slope = torch.zeros(N, device=dev)
        self.way = torch.zeros(N, device=dev)
        self.cls = torch.zeros(N, dtype=torch.long, device=dev)
        self.bump_hz = torch.zeros(N, device=dev)
        self.friction = _grow(env.mw, "geom_friction", N)  # (N, ngeom, 3), a view of the model's field
        self._fric0 = self.friction.clone()  # what each world's friction was before the first class
        self.slope_lo = torch.tensor([c.slope_deg[0] for c in self.classes], device=dev)
        self.slope_hi = torch.tensor([c.slope_deg[1] for c in self.classes], device=dev)
        self.fric = torch.tensor([c.friction for c in self.classes], device=dev)
        self.hz = torch.tensor([c.bump_hz for c in self.classes], device=dev)
        self.resample(torch.ones(N, dtype=torch.bool, device=dev))

    def resample(self, mask: torch.Tensor) -> None:
        """A new floor for the worlds in mask (N,): class, slope, gravity and friction, drawn again."""
        N, dev, e = self.env.N, self.env.device, self.env
        u = torch.rand(N, generator=e.rng, device=dev)
        cls = torch.searchsorted(self.cum, u).clamp_max(len(self.classes) - 1)
        self.cls.copy_(torch.where(mask, cls, self.cls))
        a, b = self.slope_lo[self.cls], self.slope_hi[self.cls]
        self.slope.copy_(torch.where(mask, a + (b - a) * torch.rand(N, generator=e.rng, device=dev), self.slope))
        self.way.copy_(torch.where(mask, e._u(N, lo=-math.pi, hi=math.pi), self.way))
        self.bump_hz.copy_(torch.where(mask, self.hz[self.cls], self.bump_hz))
        t = torch.deg2rad(self.slope)
        down = torch.stack([torch.sin(t) * torch.cos(self.way), torch.sin(t) * torch.sin(self.way), -torch.cos(t)], -1)
        self.gvec.copy_(down * (self.mag * self.scalevec)[:, None])
        self.gw.copy_(down.unsqueeze(1))  # the observation's down, in place: _base expands it to (N,K,3)
        self.friction.copy_(self._fric0 * self.fric[self.cls][:, None, None])

    def bump(self) -> None:
        """Uneven ground: now and then a root gets a small vertical kick, as a foot landing on a stone would give it."""
        N, dev = self.env.N, self.env.device
        if not bool((self.bump_hz > 0.0).any()):
            return
        for _k, rv in enumerate(self.env.root_v):
            hit = (torch.rand(N, generator=self.env.rng, device=dev) < self.bump_hz * self.env.dt).float()
            kick = (torch.rand(N, generator=self.env.rng, device=dev) * 1.5 - 0.5) * self.r.bump_mps
            self.env.qvel[:, rv + 2] += hit * kick

    def stats(self) -> dict:
        """The share of the batch on each class, for the run's log line."""
        share = torch.bincount(self.cls, minlength=len(self.classes)).float() / max(1, self.env.N)
        return {f"terrain_{c.name}": float(share[i].item()) for i, c in enumerate(self.classes)}
