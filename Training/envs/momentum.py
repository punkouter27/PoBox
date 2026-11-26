"""Momentum-aware turning for the footwork rung (train_box.py --momentum-w / --lean-w / --turn-accel).

Two things a body has that the plain yaw command ignored:

  * the command itself can only change rate at so many rad/s^2 (`--turn-accel`, default 12), so a turn begins with a
    wind-up and ends with a follow-through rather than a step change. The policy that follows it learns to lead the
    turn instead of reacting to it;
  * the pelvis has to lean into the turn: a body running a curve of radius v / w leans by atan(v w / g), which at the
    pelvis is an offset of height x v x w / g. `--lean-w` charges the squared miss of that offset (rt_lean); the
    angular-velocity jerk beyond what the command asks for is charged as rt_mom.

Both weights are 0 by default: with neither set nothing here is made, and the command is exactly the drawn one.
"""

from __future__ import annotations

import torch

from . import limits

G = limits.G


class TurnMomentum:
    """env.mom: the rate-limited yaw command and the terms that pay for leaning into a turn."""

    def __init__(self, env, w_mom: float, w_lean: float, accel: float):
        self.env, self.w_mom, self.w_lean, self.accel = env, float(w_mom), float(w_lean), float(accel)
        N, K, dev = env.N, env.K, env.device
        self.rate = torch.zeros(N, K, device=dev)
        self.prev_rate = torch.zeros(N, K, device=dev)
        self.prev_yaw_vel = torch.zeros(N, K, device=dev)

    def command(self, cmd: torch.Tensor) -> torch.Tensor:
        """The yaw command advanced by at most `accel` rad/s^2 this step; the other two numbers unchanged."""
        target = cmd[..., 2]
        self.prev_rate.copy_(self.rate)
        step = self.accel * self.env.dt  # the same as limits.yaw_rate_step, batched and in place
        self.rate.add_((target - self.rate).clamp(-step, step))
        return torch.cat([cmd[..., :2], self.rate[..., None]], -1)

    def terms(self, r: dict, g, lin_b: torch.Tensor, ang_b: torch.Tensor) -> None:
        """rt_mom (yaw jerk beyond the command's own ramp) and rt_tilt (miss of the centripetal lean)."""
        e = self.env
        first = (e.episode_len == 0.0)[:, None]
        yaw_vel = ang_b[..., 2]
        jerk = torch.where(first, torch.zeros_like(yaw_vel), (yaw_vel - self.prev_yaw_vel) / e.dt)
        self.prev_yaw_vel.copy_(yaw_vel)
        if self.w_mom > 0.0:
            r["mom"] = -self.w_mom * (jerk.abs() - self.accel).clamp_min(0.0).pow(2)
        if self.w_lean > 0.0:
            pos, feet = g["pos"], g["foot_xy"].mean(-2)
            c, s = torch.cos(g["yaw"]), torch.sin(g["yaw"])
            dx, dy = pos[..., 0] - feet[..., 0], pos[..., 1] - feet[..., 1]
            lateral = -dx * s + dy * c  # sideways in the fighter's own heading
            height = (pos[..., 2] - g["sole_z"].mean(-1)).clamp(0.2, 2.0)
            want = height * lin_b[..., 0] * yaw_vel / G
            r["tilt"] = -self.w_lean * (lateral - want).pow(2)

    def reset(self, mask: torch.Tensor) -> None:
        """A new episode begins from rest, as a fresh turn command does."""
        m1 = mask[:, None]
        z = torch.zeros_like(self.rate)
        self.rate.copy_(torch.where(m1, z, self.rate))
        self.prev_rate.copy_(torch.where(m1, z, self.prev_rate))
        self.prev_yaw_vel.copy_(torch.where(m1, z, self.prev_yaw_vel))
