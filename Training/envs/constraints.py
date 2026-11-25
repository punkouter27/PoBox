"""Hard, opt-in physical limits on the batched drives: gravity scaling, motor torque caps, actuator lag and compliant
joint stops (train_box.py --gravity-scale / --torque-cap / --act-lag / --soft-limit; formulas in envs/limits.py).

Everything here is drawn once, when the env is made (a value a world, a variant of the body), and then written in place
every step, so the CUDA graphs (the env's own and envs/step_graph.py's) see it. Nothing is mirrored in the game: the game
runs the nominal body, so these only make a brain robust to a body that is not exactly the one in the file. With every
range at its default nothing here is made at all (env.phys stays None).
"""

from __future__ import annotations

import math

import numpy as np
import torch
import warp as wp

from . import limits


class Constraints:
    """env.phys: the limits of every world, applied in BoxingOpsMixin._drive and charged in _torque_terms."""

    def __init__(self, env, r: limits.PhysRanges):
        self.env, self.r = env, r
        self.N, self.K, self.A, dev = env.N, env.K, env.A, env.device
        self.lag_on = r.act_lag_hz > 0.0
        self.cap_on = r.torque_cap < 1.0 or r.torque_var > 0.0
        self.stop_on = r.soft_limit > 0.0
        self.margin = float(r.soft_limit)

        def u(*shape, lo, hi):
            return lo + torch.rand(*shape, generator=env.rng, device=dev) * (hi - lo)

        # ---- gravity: g x (1 + s) a world. The observation's down is that same g, or a body on another planet would
        # read "which way is down" wrongly and never learn to stand under the change. Every write is in place: the terrain
        # (envs/terrain.py) holds the same arrays and re-draws them at every reset, so the objects must not be replaced.
        down = env.gravity_world
        down = down.reshape(-1, 3) if down.dim() > 1 else down.view(1, 3)
        down = (down / down.norm(dim=-1, keepdim=True).clamp_min(1e-6)).to(dev)
        if down.shape[0] != self.N:
            down = down.expand(self.N, 3)
        self.mag = float(np.linalg.norm(env.m.opt.gravity))
        self.scale = u(self.N, lo=1.0 - r.gravity, hi=1.0 + r.gravity) if r.gravity else torch.ones(self.N, device=dev)
        if r.gravity:
            vec = down * (self.mag * self.scale)[:, None]
            gw = env.gravity_world
            if tuple(gw.shape) == (self.N, 1, 3):
                gw.copy_(down.unsqueeze(1))
            else:
                env.gravity_world = down.unsqueeze(1)  # (N,1,3): _base expands it to (N,K,3)
            if env.mw.opt.gravity.shape[0] == self.N:
                wp.to_torch(env.mw.opt.gravity).copy_(vec)  # the terrain's array, kept
            else:
                env.mw.opt.gravity = wp.array(
                    vec.detach().cpu().numpy().astype(np.float32), dtype=wp.vec3, device=env.mw.opt.gravity.device
                )
        if env.terrain is not None:
            env.terrain.scalevec.copy_(self.scale)  # the floor redraws gravity every episode: keep this world's g

        # ---- motor torque caps: the drive's force range, scaled a world, and enforced by the physics as well.
        n = self.K * self.A
        share = r.torque_cap * u(self.N, self.K, self.A, lo=1.0 - r.torque_var, hi=1.0 + r.torque_var)
        # A drive with no force range (0, 0) in the model is unbounded in MuJoCo: capping it there would freeze the joint.
        free = (env.f_hi - env.f_lo).abs() < 1e-6
        inf = torch.full_like(env.f_hi, float("inf"))
        self.cap_lo = torch.where(free, -inf, env.f_lo * share)  # (N,K,A), the negative side of each drive's range
        self.cap_hi = torch.where(free, inf, env.f_hi * share)
        if self.cap_on:
            fr = env.mw.actuator_forcerange
            if fr.shape[0] != self.N:
                host = np.repeat(fr.numpy(), self.N, 0)
                env.mw.actuator_forcerange = wp.array(host, dtype=fr.dtype, device=fr.device)
            self.forcerange = wp.to_torch(env.mw.actuator_forcerange)
            self.forcerange[:, :n, 0] = torch.where(torch.isfinite(self.cap_lo), self.cap_lo, env.f_lo).reshape(
                self.N, n
            )
            self.forcerange[:, :n, 1] = torch.where(torch.isfinite(self.cap_hi), self.cap_hi, env.f_hi).reshape(
                self.N, n
            )

        # ---- motor lag: a first-order filter on the target, a fighter (one bandwidth a body variant)
        hz = r.act_lag_hz * u(self.K, lo=1.0 - r.lag_var, hi=1.0 + r.lag_var)
        self.alpha = (1.0 - torch.exp(-2.0 * math.pi * hz * env.dt)).view(1, self.K, 1)
        self.lag = torch.zeros(self.N, self.K, self.A, device=dev)
        self.rom = torch.zeros(self.N, self.K, self.A, device=dev)

    # ---- step ----------------------------------------------------------------------------------------
    def drive(self, want: torch.Tensor, q: torch.Tensor, kp: torch.Tensor) -> torch.Tensor:
        """The drive targets (N,K,A) after the motor's lag, its torque cap and its compliant stops."""
        if self.lag_on:
            self.lag += self.alpha * (want - self.lag)  # in place: step_graph pins only the env's own attributes
            want = self.lag
        if self.cap_on:
            torque = (kp * (want - q)).clamp(min=self.cap_lo, max=self.cap_hi)
            want = q + torque / kp.clamp_min(1e-6)
        if self.stop_on:
            m = self.margin
            lo, hi = self.env.joint_lo, self.env.joint_hi
            g = torch.maximum((q - (hi - m)) / m, ((lo + m) - q) / m).clamp(0.0, 1.0)
            torch.maximum(self.rom, g, out=self.rom)  # in place: a captured step graph must keep the same tensor
            # At the limit the target is the limit, so the drive stops pushing instead of fighting the stop.
            want = want * (1.0 - g) + torch.where((hi - q) < (q - lo), hi, lo) * g
        return want

    def terms(self, r: dict) -> None:
        """The charge for working a joint into the last radians of its range (rt_rom), only when asked for."""
        if self.stop_on and self.r.rom_w > 0.0:
            r["rom"] = -self.r.rom_w * self.rom.sum(-1)

    def reset(self, mask: torch.Tensor, q: torch.Tensor) -> None:
        """A new episode begins where its body is: the lag's history and the range-of-motion memory start afresh."""
        m3 = mask[:, None, None]
        self.lag.copy_(torch.where(m3, q, self.lag))
        if self.stop_on:
            self.rom.copy_(torch.where(m3, torch.zeros_like(self.rom), self.rom))

    def stats(self) -> dict:
        """How much of each joint's range the batch is using, and the gravity it feels, for the run's log line."""
        out = {"gravity_scale": float(self.scale.mean().item())}
        if self.stop_on:
            out["rom_use"] = float(self.rom.mean().item())
        return out
