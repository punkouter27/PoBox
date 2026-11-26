"""Obstacle navigation for the footwork rung (train_box.py --nav-share; ranges in config/progression.json).

A share of the walk episodes is replaced by a navigation episode: a goal point two to four metres away, a couple of
the floor's cubes parked across the straight line to it, and the command drawn toward the goal every step (the same
three command numbers the observation always carries, so nothing about the policy's input changes). The reward is per
m/s of progress toward the goal, a bonus on arrival, and a charge for shoving a parked obstacle out of the way. Arriving
ends the episode as a time-out does (the value is carried over), so reaching the goal is not a termination to the critic.

The cubes are held at their parking places every step by envs/footwork.py's _cubes, so an obstacle stays where it was put.
"""

from __future__ import annotations

import torch

from . import limits


class NavGoals:
    """env.nav_env: goals, obstacles and the steering command of the navigation episodes."""

    def __init__(self, env, r: limits.NavRanges):
        self.env, self.r = env, r
        N, K, C, dev = env.N, env.K, env.C, env.device
        self.goal = torch.zeros(N, K, 2, device=dev)
        self.prev_dist = torch.zeros(N, K, device=dev)
        self.arrived = torch.zeros(N, K, dtype=torch.bool, device=dev)
        self.max_obs = min(int(r.obstacles[1]), C)
        self.fractions = torch.tensor([0.35, 0.55, 0.75], device=dev)[: self.max_obs]
        self.obs_qi = env.cube_qi[: self.max_obs]  # (max_obs, 7)
        self.prev_xy = torch.zeros(N, self.max_obs, 2, device=dev)
        self.obstacle_w = 2.0  # charge per m/s an obstacle is shoved

    def draw(self, mask: torch.Tensor) -> None:
        """New goals (and obstacles) for the worlds in mask, and the cubes' parking places with them."""
        N, K, dev, e = self.env.N, self.env.K, self.env.device, self.env
        nav = e.nav
        pos = torch.stack([e.qpos[:, rq : rq + 2] for rq in e.root_q], 1)  # (N,K,2), where the episode begins
        bearing = e._u(N, K, lo=-3.14, hi=3.14)
        dist = e._u(N, K, lo=2.0, hi=4.0)
        goal = pos + torch.stack([torch.cos(bearing), torch.sin(bearing)], -1) * dist[..., None]
        lim = e.ring_half - 0.6
        goal = goal.clamp(-lim, lim)
        self.goal.copy_(torch.where(nav[..., None], goal, self.goal))
        self.prev_dist.copy_(torch.where(nav, (self.goal - pos).norm(dim=-1), self.prev_dist))
        self.arrived.copy_(self.arrived & ~nav)
        if not self.max_obs:
            return
        # Obstacles: a few of the pool's cubes parked across the line, a little to either side, on the floor.
        n_obs = torch.randint(int(self.r.obstacles[0]), self.max_obs + 1, (N,), generator=e.rng, device=dev)
        side = (torch.rand(N, self.max_obs, generator=e.rng, device=dev) * 2.0 - 1.0) * 0.45
        d = goal - pos[:, :1]  # (N,1,2): the line the first fighter would walk
        d = d / d.norm(dim=-1, keepdim=True).clamp_min(1e-3)
        perp = torch.stack([-d[..., 1], d[..., 0]], -1)
        park = e.cube_park  # (N, C, 7)
        quat = torch.tensor([1.0, 0.0, 0.0, 0.0], device=dev).expand(N, 4)
        for j in range(self.max_obs):
            q = pos[:, 0] + d[:, 0] * (dist[:, :1] * self.fractions[j]) + perp[:, 0] * side[:, j : j + 1]
            row = torch.cat([q.clamp(-lim, lim), park[:, j, 2:3]], -1)  # (N,3): kept on the floor
            use = (nav[:, 0] & (n_obs > j))[:, None]
            park[:, j] = torch.where(use, torch.cat([row, quat], -1), park[:, j])
        self.prev_xy.copy_(e.qpos[:, self.obs_qi][..., :2])

    def command(self, cmd: torch.Tensor, g) -> torch.Tensor:
        """The walk command replaced by a steering command in the navigation episodes, every step."""
        e = self.env
        pos, yaw = g["pos"], g["yaw"]
        d = self.goal - pos[..., :2]
        c, s = torch.cos(yaw), torch.sin(yaw)
        fwd = d[..., 0] * c + d[..., 1] * s
        side = -d[..., 0] * s + d[..., 1] * c
        turn = (2.0 * torch.atan2(side, fwd.clamp_min(1e-3))).clamp(-1.0, 1.0)
        out = torch.stack([fwd.clamp(-0.4, float(e.cmd_fwd_max)), (side * 0.8).clamp(-0.4, 0.4), turn], -1)
        return torch.where(e.nav[..., None], out, cmd)

    def terms(self, r: dict, g) -> torch.Tensor:
        """rt_goal: progress, arrival and the obstacles shoved; returns the fighters that arrived (N, K)."""
        e = self.env
        nav = e.nav.float()
        pos = g["pos"][..., :2]
        d = (self.goal - pos).norm(dim=-1)
        progress = ((self.prev_dist - d) / e.dt).clamp(-3.0, 3.0)
        self.prev_dist.copy_(d)
        self.arrived.copy_(e.nav & (d < self.r.reach_m))
        r["goal"] = nav * (self.r.reward * progress + self.r.arrive * self.arrived.float())
        if self.max_obs:
            xy = e.qpos[:, self.obs_qi][..., :2]
            drift = (xy - self.prev_xy).norm(dim=-1).sum(-1, keepdim=True).clamp_max(5.0) / e.dt
            self.prev_xy.copy_(xy)
            r["goal"] = r["goal"] - nav * self.obstacle_w * drift
        return self.arrived
