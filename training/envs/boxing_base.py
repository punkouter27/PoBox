"""Simulation graph, reset, geometry, observation, and actuator drive ops for BoxingEnv."""
from __future__ import annotations

import math
from typing import Dict, Tuple

import mujoco_warp as mjw
import torch
import warp as wp

from .math3d import quat_mul, quat_rotate_inverse, quat_yaw, to_heading, yaw_quat


class BoxingOpsMixin:
    """Reusable physics-wrapper methods mixed into BoxingEnv and its stage subclasses."""

    # ---- for a subclass that is another stage of training ---------------------------------------
    def _prepare_model(self, m) -> None:
        """Changes to the model before it goes to the GPU."""

    def _init_extra(self) -> None:
        """State of its own, made before the first reset."""

    # ---- CUDA graphs ---------------------------------------------------------------------------
    def _capture_graphs(self) -> None:
        if not self.use_cuda_graph:
            return
        try:
            for _ in range(self.decimation):
                mjw.step(self.mw, self.dw)
            mjw.forward(self.mw, self.dw)
            wp.synchronize()
            with wp.ScopedCapture() as cap:
                for _ in range(self.decimation):
                    mjw.step(self.mw, self.dw)
            self._step_graph = cap.graph
            with wp.ScopedCapture() as cap:
                mjw.forward(self.mw, self.dw)
            self._fwd_graph = cap.graph
        except Exception as e:   # capture is an optimisation, never a requirement
            print(f"[cuda-graph] capture unavailable ({e}); falling back to direct launches")
            self._step_graph = self._fwd_graph = None

    def _physics_step(self) -> None:
        if self._step_graph is not None:
            wp.capture_launch(self._step_graph)
        else:
            for _ in range(self.decimation):
                mjw.step(self.mw, self.dw)

    def _physics_forward(self) -> None:
        if self._fwd_graph is not None:
            wp.capture_launch(self._fwd_graph)
        else:
            mjw.forward(self.mw, self.dw)

    # ---- random -------------------------------------------------------------------------------
    def _u(self, *shape, lo: float = 0.0, hi: float = 1.0) -> torch.Tensor:
        return lo + torch.rand(*shape, generator=self.rng, device=self.device) * (hi - lo)

    # ---- reset --------------------------------------------------------------------------------
    def _reset_states(self) -> Tuple[torch.Tensor, torch.Tensor]:
        N, A = self.N, self.A
        q = self.default_qpos.unsqueeze(0).repeat(N, 1)
        v = torch.zeros(N, self.qvel.shape[1], device=self.device)

        if self.spar:
            # Somewhere in the ring, some distance apart, roughly facing each other.
            centre = self._u(N, 2, lo=-1.4, hi=1.4)
            bearing = self._u(N, lo=-math.pi, hi=math.pi)
            sep = self._u(N, lo=0.85, hi=self.sep_hi)
            u = torch.stack([torch.cos(bearing), torch.sin(bearing)], -1)
            spots = [centre - u * sep[:, None] * 0.5, centre + u * sep[:, None] * 0.5]
            facing = [bearing, bearing + math.pi]
            if self.ropes_start > 0.0:
                # As the free-for-all's rounds begin (FreeForAll.ToTheRopes): spots round the square of the ropes, half a
                # metre inside, 16 of them; the other boxer two to eight spots round from this one; both facing the middle.
                def rope_spot(i):
                    a = i.float() * (2.0 * math.pi / 16.0)
                    c, s = torch.cos(a), torch.sin(a)
                    return torch.stack([c, s], -1) / torch.maximum(c.abs(), s.abs())[:, None] * (self.ring_half - 0.5)
                i0 = torch.randint(0, 16, (N,), device=self.device, generator=self.rng)
                i1 = (i0 + torch.randint(2, 9, (N,), device=self.device, generator=self.rng)) % 16
                use = self._u(N) < self.ropes_start
                for k, i in enumerate((i0, i1)):
                    p = rope_spot(i)
                    spots[k] = torch.where(use[:, None], p, spots[k])
                    facing[k] = torch.where(use, torch.atan2(-p[:, 1], -p[:, 0]), facing[k])
        else:
            # Somewhere round the bag, within a step or two of punching range, roughly facing it.
            bearing = self._u(N, lo=-math.pi, hi=math.pi)
            dist = self._u(N, lo=0.65, hi=1.35)
            u = torch.stack([torch.cos(bearing), torch.sin(bearing)], -1)
            spots = [-u * dist[:, None]]
            facing = [bearing]
            tilt = self._u(N, 3, lo=-0.06, hi=0.06)
            bq = torch.cat([torch.ones(N, 1, device=self.device), tilt[:, :2], torch.zeros(N, 1, device=self.device)], -1)
            q[:, self.bag_q:self.bag_q + 4] = bq / bq.norm(dim=-1, keepdim=True)
            v[:, self.bag_v:self.bag_v + 2] = self._u(N, 2, lo=-0.3, hi=0.3)

        for k in range(self.K):
            rq, rv = self.root_q[k], self.root_v[k]
            yaw = facing[k] + self._u(N, lo=-0.6, hi=0.6)
            q[:, rq:rq + 2] = spots[k]
            q[:, rq + 2] = self.stand_height[k] + 0.002
            q[:, rq + 3:rq + 7] = yaw_quat(yaw)
            s = self.start_spread
            q[:, self.jq[k]] = (self.default_joint[k] + self._u(N, A, lo=-0.06 * s, hi=0.06 * s)).clamp(self.joint_lo[k], self.joint_hi[k])
            v[:, rv:rv + 2] = self._u(N, 2, lo=-0.15 * s, hi=0.15 * s)
            b = self.bank[k]
            if b is not None:
                pick = torch.randint(0, b["z"].shape[0], (N,), device=self.device, generator=self.rng)
                use = self._u(N) < self.handover_share
                self._bank_use[:, k] = use
                u1 = use[:, None]
                if self.handover_yaw > 0.6:
                    # 2026-11-02 (tools/carry_diag_v3.py): a boxer gets up facing wherever it fell. Nick's falls after the
                    # hand-over were side-on or turned away (facing -0.07, against 0.72 for those that stayed up), and the
                    # bank's starts all faced the other boxer within 0.6 rad. handover_yaw widens that spread.
                    yaw = facing[k] + self._u(N, lo=-self.handover_yaw, hi=self.handover_yaw)
                q[:, self.jq[k]] = torch.where(u1, b["joint"][pick], q[:, self.jq[k]])
                q[:, rq + 2] = torch.where(use, b["z"][pick] + 0.002, q[:, rq + 2])
                q[:, rq + 3:rq + 7] = torch.where(u1, quat_mul(yaw_quat(yaw), b["tilt"][pick]), q[:, rq + 3:rq + 7])
                c, s = torch.cos(yaw), torch.sin(yaw)
                lin = b["lin"][pick]
                world = torch.stack([c * lin[:, 0] - s * lin[:, 1], s * lin[:, 0] + c * lin[:, 1], lin[:, 2]], -1)
                v[:, rv:rv + 3] = torch.where(u1, world, v[:, rv:rv + 3])
                v[:, rv + 3:rv + 6] = torch.where(u1, b["ang"][pick], v[:, rv + 3:rv + 6])
                v[:, self.jv[k]] = torch.where(u1, b["jvel"][pick], v[:, self.jv[k]])
        return q, v

    def _apply_reset(self, mask: torch.Tensor) -> None:
        q, v = self._reset_states()
        m1 = mask[:, None]
        self.qpos.copy_(torch.where(m1, q, self.qpos))
        self.qvel.copy_(torch.where(m1, v, self.qvel))
        if self.n_bodies:
            self._place_bodies(mask, q)
        ctrl = torch.cat([q[:, self.jq[k]] for k in range(self.K)], dim=-1)
        self.ctrl.copy_(torch.where(m1, ctrl, self.ctrl))
        m3 = mask[:, None, None]
        self.last_action = torch.where(m3, torch.zeros_like(self.last_action), self.last_action)
        self.prev_action = torch.where(m3, torch.zeros_like(self.prev_action), self.prev_action)
        self.held = torch.where(m3, ctrl.view(self.N, self.K, self.A), self.held)
        self.from_bank = torch.where(m1, self._bank_use, self.from_bank)
        self.step_count = torch.where(mask, torch.zeros_like(self.step_count), self.step_count)
        self.episode_len = torch.where(mask, torch.zeros_like(self.episode_len), self.episode_len)
        self.episode_return = torch.where(m1, torch.zeros_like(self.episode_return), self.episode_return)
        self.armed = torch.where(m3, torch.ones_like(self.armed), self.armed)
        self.cool = torch.where(m3, torch.zeros_like(self.cool), self.cool)
        self.prev_closing = torch.where(mask[:, None, None, None], torch.zeros_like(self.prev_closing), self.prev_closing)
        self.since_hit = torch.where(m1, torch.full_like(self.since_hit, 99.0), self.since_hit)
        self.head_vel = torch.where(m3, torch.zeros_like(self.head_vel), self.head_vel)
        self.daze = torch.where(m1, torch.zeros_like(self.daze), self.daze)
        self.legs_out = torch.where(m1, torch.zeros_like(self.legs_out), self.legs_out)
        self.drive_scale = torch.where(m1, torch.ones_like(self.drive_scale), self.drive_scale)
        self.prev_block = torch.where(m3, torch.zeros_like(self.prev_block), self.prev_block)
        self.since_block = torch.where(m1, torch.full_like(self.since_block, 99.0), self.since_block)
        if self.fatigue_on:
            # An episode is twelve seconds and a round is forty-five: it begins anywhere in a round's tiredness.
            self.reserve = torch.where(m1 & self.tires, self._u(self.N, self.K, lo=0.3, hi=1.0), self.reserve)
            if self.handover_reserve < 1.0:
                # 2026-11-02: a hand-over comes after a get-up, and the body is spent by then: the game gets up from
                # 0.5 with nothing drained while rising, the C exam from a full reserve drained by the rise. The
                # bank's states carry no reserve; they begin with 0 to handover_reserve of it.
                tired = self._u(self.N, self.K, lo=0.0, hi=self.handover_reserve)
                self.reserve = torch.where(m1 & self.tires & self.from_bank, tired, self.reserve)
        # And anywhere in a fight's punishment.
        self.hurt = torch.where(m1, self._u(self.N, self.K, lo=0.0, hi=0.8) * self.hurt_full, self.hurt)
        self.stagger = torch.where(m1, torch.zeros_like(self.stagger), self.stagger)
        self.push = torch.where(m1[..., None], torch.zeros_like(self.push), self.push)
        self.legs_at = torch.where(m1, self.daze_hi * (1.0 - self.hurt_hi * self._hurt01()), self.legs_at)
        self.last_kind = torch.where(m1, torch.full_like(self.last_kind, -1), self.last_kind)
        self.streak = torch.where(m1, torch.zeros_like(self.streak), self.streak)
        self.since_punch = torch.where(m3, torch.full_like(self.since_punch, 99.0), self.since_punch)
        if self.hurt_start > 0.0 and self.daze_on:
            # Begun hurt: most of the way to the legs going, so a clinch (or a careful round) is worth something.
            hurt = m1 & (self._u(self.N, self.K) < self.hurt_start)
            self.daze = torch.where(hurt, self._u(self.N, self.K, lo=0.55, hi=0.9) * self.legs_at, self.daze)

    def _place_bodies(self, mask: torch.Tensor, q: torch.Tensor) -> None:
        """Each downed body somewhere in the ring at least 0.9 m from both fighters' spots and 1.2 m from the other
        bodies (the first of 8 tries that is), turned any way; a quarter of the time, or with no try clear, parked away."""
        N, dev = self.N, self.device
        lim = self.ring_half - 0.7
        feet = [q[:, self.root_q[k]:self.root_q[k] + 2] for k in range(self.K)]
        placed = []
        for j in range(self.n_bodies):
            tries = self._u(N, 8, 2, lo=-lim, hi=lim)
            ok = torch.ones(N, 8, dtype=torch.bool, device=dev)
            for f in feet:
                ok &= (tries - f[:, None]).norm(dim=-1) > 0.9
            for p in placed:
                ok &= (tries - p[:, None]).norm(dim=-1) > 1.2
            pick = ok.float().argmax(-1)
            xy = tries[torch.arange(N, device=dev), pick]
            there = ok.any(-1) & (self._u(N) > 0.25)
            away = torch.tensor([30.0 + 5.0 * j, 30.0], device=dev).expand(N, 2)
            xy = torch.where(there[:, None], xy, away)
            placed.append(torch.where(there[:, None], xy, torch.full_like(xy, 1e3)))
            pos = torch.cat([xy, torch.zeros(N, 1, device=dev)], -1)
            self.mocap_pos[:, j] = torch.where(mask[:, None], pos, self.mocap_pos[:, j])
            self.mocap_quat[:, j] = torch.where(mask[:, None], yaw_quat(self._u(N, lo=-math.pi, hi=math.pi)), self.mocap_quat[:, j])

    def _reseed_trackers(self, mask: torch.Tensor) -> None:
        """Positions remembered for finite differences have to follow a reset, or the first step of
        every episode reads a velocity of (new position - old position) / dt: hundreds of metres a second."""
        g = self._geometry()
        m3, m4 = mask[:, None, None], mask[:, None, None, None]
        self.prev_glove = torch.where(m4, g["glove"], self.prev_glove)
        self.prev_head = torch.where(m3, g["t_head"], self.prev_head)
        self.prev_body = torch.where(m3, g["t_body"], self.prev_body)
        self.prev_foot_xy = torch.where(m4, g["foot_xy"], self.prev_foot_xy)
        if self.spar:
            self.prev_pinned = torch.where(mask[:, None], self._pinned(g["pos"][..., :2]), self.prev_pinned)

    def _pinned(self, xy: torch.Tensor) -> torch.Tensor:
        """How far into the last metre before each rope a pelvis is, summed over the ropes (N, K): 0 a metre or more off,
        about 0.7 against one (a pelvis gets no nearer), so a corner, two ropes at once, counts twice. The ring's centre is (0, 0)."""
        return (xy.abs() - (self.ring_half - 1.0)).clamp(0.0, 1.0).sum(-1)

    def _take(self, action: torch.Tensor) -> torch.Tensor:
        """The policy's action (N*K, A) as the drives get it, (N, K, A): clipped, then smoothed with the fighter's
        action_filter. It becomes the observation's last action. Every stage's step takes its action through here."""
        action = action.clamp(-self.action_clip, self.action_clip).reshape(self.N, self.K, self.A)
        if self.filter_on:
            action = self.act_filter * self.last_action + (1.0 - self.act_filter) * action
        self.prev_action = self.last_action
        self.last_action = action
        return action

    def reset(self) -> torch.Tensor:
        all_envs = torch.ones(self.N, dtype=torch.bool, device=self.device)
        self._apply_reset(all_envs)
        self._physics_forward()
        self._reseed_trackers(all_envs)
        # Spread the episode clock, so the population does not time out in lockstep for ever.
        self.step_count = torch.randint(0, max(1, self.max_steps), (self.N,), device=self.device, generator=self.rng).float()
        # Those clocks are not time since a hand-over: recover_w and carry_on start counting at the next resets.
        self.from_bank = torch.zeros_like(self.from_bank)
        self._obs = self._observe(self._geometry())
        return self._obs

    # ---- geometry ------------------------------------------------------------------------------
    def _geometry(self) -> Dict[str, torch.Tensor]:
        """Everything the observation and the reward read off the bodies, shaped (N, K, ...)."""
        g: Dict[str, torch.Tensor] = {}
        g["pos"] = self.xpos[:, self.pelvis]                               # (N,K,3)
        g["quat"] = self.xquat[:, self.pelvis]                             # (N,K,4)
        g["yaw"] = quat_yaw(g["quat"])
        g["glove"] = self.geom_xpos[:, self.g_glove]                       # (N,K,2,3)
        head = self.geom_xpos[:, self.g_head]                              # (N,K,3)
        tc = self.geom_xpos[:, self.g_torso]
        tax = self.geom_xmat[:, self.g_torso][..., :, 2]                   # capsule axis, world
        g["own_head"], g["own_body"], g["own_axis"] = head, tc, tax

        if self.spar:
            # Each fighter's target is the other one.
            g["t_head"], g["t_body"], g["t_axis"] = head.flip(1), tc.flip(1), tax.flip(1)
            g["opp_glove"], g["opp_yaw"] = g["glove"].flip(1), g["yaw"].flip(1)
            # The sizes are the other fighter's, which in a match between two bodies are not one's own.
            shape = lambda t: t.flip(0).view(1, self.K, 1)
            g["t_r_head"], g["t_r_body"] = shape(self.r_head_k), shape(self.r_torso_k)
            g["own_r_head"] = self.r_head_k.view(1, self.K, 1)
            g["t_half_head"], g["t_half"] = 0.0, shape(self.torso_half_k)   # a head is a ball, a body a capsule
        else:
            axis = self.geom_xmat[:, self.g_bag][:, :, 2]                  # (N,3)
            g["t_head"] = self.site_xpos[:, self.s_bag_head][:, None]
            g["t_body"] = self.site_xpos[:, self.s_bag_body][:, None]
            g["t_axis"] = axis[:, None]
            # The bag is one capsule; its top half stands in for a head and its bottom half for a body.
            g["t_r_head"], g["t_r_body"] = self.r_bag, self.r_bag
            g["t_half_head"], g["t_half"] = 0.2, 0.2

        cz = self.geom_xpos[:, self.g_foot][..., 2]                        # (N,K,2)
        rz = self.geom_xmat[:, self.g_foot][..., 2, :]                     # world-z row of each foot
        g["sole_z"] = cz - (rz.abs() * self.foot_half).sum(-1)
        g["foot_xy"] = self.geom_xpos[:, self.g_foot][..., :2]
        g["foot_contact"] = g["sole_z"] < 0.005
        return g

    def _base(self, g):
        N, K = self.N, self.K
        lin_w = torch.stack([self.qvel[:, rv:rv + 3] for rv in self.root_v], 1)            # (N,K,3)
        ang_b = torch.stack([self.qvel[:, rv + 3:rv + 6] for rv in self.root_v], 1)
        q = g["quat"]
        lin_b = quat_rotate_inverse(q, lin_w)
        grav_b = quat_rotate_inverse(q, self.gravity_world.expand(N, K, 3))
        return lin_w, lin_b, ang_b, grav_b

    def _observe(self, g) -> torch.Tensor:
        N, K = self.N, self.K
        lin_w, lin_b, ang_b, grav_b = self._base(g)
        pos, yaw = g["pos"], g["yaw"]
        rel = lambda p: to_heading(p - pos, yaw)
        head_vel = self.head_vel - lin_w
        own_gloves = to_heading(g["glove"] - pos[:, :, None], yaw[:, :, None]).reshape(N, K, 6)
        if self.spar:
            opp_gloves = to_heading(g["opp_glove"] - pos[:, :, None], yaw[:, :, None]).reshape(N, K, 6)
            dyaw = g["opp_yaw"] - yaw
            facing = torch.stack([torch.cos(dyaw), torch.sin(dyaw)], -1)
        else:
            opp_gloves = torch.zeros(N, K, 6, device=self.device)
            facing = torch.zeros(N, K, 2, device=self.device)
        ring = to_heading(torch.cat([-pos[..., :2], torch.zeros(N, K, 1, device=self.device)], -1), yaw)[..., :2] / self.ring_half

        jp = self.qpos[:, self.jq] - self.default_joint
        jv = self.qvel[:, self.jv]
        obs = torch.cat([lin_b, ang_b, grav_b, jp, jv, self.last_action,
                         g["foot_contact"].float(), pos[..., 2:3],
                         rel(g["t_head"]), rel(g["t_body"]), to_heading(head_vel, yaw),
                         own_gloves, opp_gloves, facing, ring], dim=-1)
        if self.obs_noise > 0.0:
            obs = obs + torch.randn(obs.shape, generator=self.rng, device=self.device) * (self.noise_scale * self.obs_noise)
        return torch.nan_to_num(obs).clamp(-100.0, 100.0).reshape(N * K, self.obs_dim)

    def _delayed(self, obs: torch.Tensor) -> torch.Tensor:
        """The observation as it was 0 to obs_delay_max control steps ago, the lag drawn per row when its episode
        begins (an episode's first steps see its first observation, never the last episode's)."""
        H = self.obs_delay_max + 1
        fresh = (self.episode_len == 0.0)[:, None].expand(self.N, self.K).reshape(-1)
        if self._obs_hist is None:
            self._obs_hist = obs[None].repeat(H, 1, 1)
            self._obs_lag = torch.zeros(obs.shape[0], dtype=torch.long, device=self.device)
        hist = torch.cat([obs[None], self._obs_hist[:-1]], 0)
        self._obs_hist = torch.where(fresh[None, :, None], obs[None].expand_as(hist), hist)
        lag = torch.randint(0, H, (obs.shape[0],), generator=self.rng, device=self.device)
        if not self._lag_plain:
            # Some fighter has a floor on its lag (obs_delay) or less room than the most: lo + 0..span-1, uniform.
            u = torch.rand(obs.shape[0], generator=self.rng, device=self.device)
            lag = self._lag_lo + (u * self._lag_span).long().clamp_max(self._lag_span - 1)
        self._obs_lag = torch.where(fresh, lag, self._obs_lag)
        out = self._obs_hist[self._obs_lag, self._rows].clone()
        a0 = 9 + 2 * self.A                                    # the last action: the boxer's own, known at once
        out[:, a0:a0 + self.A] = obs[:, a0:a0 + self.A]
        return out

    # ---- step ----------------------------------------------------------------------------------
    def _drive(self, action: torch.Tensor) -> None:
        """Joint targets from an action (N, K, A). A weakened fighter is asked for a point part of the way
        from where each joint is to where the policy wants it: the spring is weaker, the damping is not.
        The game's MujocoRing does exactly this with the same number."""
        N, K, A = self.N, self.K, self.A
        want = (self.default_joint + action * self.scale_ka).clamp(self.joint_lo, self.joint_hi)
        if self.blend_on:
            n = torch.tensor(self.blend_k, device=self.device, dtype=torch.float32).view(1, K)
            w = torch.where(n > 0, (1.0 - self.step_count.view(N, 1) / n.clamp_min(1.0)).clamp(0.0, 1.0), torch.zeros_like(n))
            want = w[..., None] * self.held + (1.0 - w[..., None]) * want
        q = self.qpos[:, self.jq]
        if self.ceiling_on:
            # A tired drive under the ceiling: the spring keeps the daze's weakening only, and the target is held where
            # the drive's force, kp (target - q) - kv qdot, stays inside what is left of its force range on each side.
            tired = self._tired()
            ceil = self.ceiling.view(1, K)
            want = q + (self.drive_scale * torch.where(ceil, torch.ones_like(tired), tired))[..., None] * (want - q)
            kp, kv = self._gains()
            share = torch.where(ceil, tired, torch.full_like(tired, 1e6))[..., None]
            base = q + kv * self.qvel[:, self.jv] / kp
            want = torch.minimum(torch.maximum(want, base + share * self.f_lo / kp), base + share * self.f_hi / kp)
        elif self.daze_on or self.MODE or self.fatigue_on:
            scale = self.drive_scale * self._tired() if self.fatigue_on else self.drive_scale
            want = q + scale[..., None] * (want - q)
        self.ctrl.copy_(want.reshape(N, K * A))

    def _gains(self):
        """Each world's kp and kv, (worlds, K, A): the bodies are randomised a world (FootworkEnv._randomise) after __init__."""
        if self._kp is None:
            g, b = wp.to_torch(self.mw.actuator_gainprm), wp.to_torch(self.mw.actuator_biasprm)
            g, b = (g[None], b[None]) if g.dim() == 2 else (g, b)
            n = self.K * self.A
            self._kp = g[:, :n, 0].reshape(-1, self.K, self.A).clone()
            self._kv = (-b[:, :n, 2]).reshape(-1, self.K, self.A).clone()
        return self._kp, self._kv

    def _selfcontact(self) -> torch.Tensor:
        """Metres of each fighter's own parts pressed into each other at the last substep, (N, K): the soft contacts'
        overlap, which grows with how hard they press. Read off the contact list on the GPU, without a sync."""
        c = self.dw.contact
        geom, dist, world = wp.to_torch(c.geom).long(), wp.to_torch(c.dist), wp.to_torch(c.worldid).long()
        live = torch.arange(dist.shape[0], device=self.device) < wp.to_torch(self.dw.nacon)[0]
        own = self.geom_owner[geom]                                            # (C, 2)
        mine = live & (own[:, 0] == own[:, 1]) & (own[:, 0] >= 0)
        pen = (-dist).clamp_min(0.0) * mine.float()
        out = torch.zeros(self.N * self.K, device=self.device)
        out.index_add_(0, (world.clamp(0, self.N - 1) * self.K + own[:, 0].clamp_min(0)), pen)
        return out.view(self.N, self.K)

    def _flail(self, jv: torch.Tensor, idle: torch.Tensor) -> torch.Tensor:
        """(rad/s)^2 of the shoulders and elbows of the hands that are idle (N, K, 2), summed: (N, K)."""
        per_hand = (jv[..., self.hand_arm] ** 2).sum(-1)                         # (N, K, 2)
        return (per_hand * idle.float()).sum(-1)

    def _spend(self, tau: torch.Tensor, jv: torch.Tensor) -> None:
        """Takes the step's joules from the reserve and lets it fill again. Every stage's step calls this: footwork
        and get-up once skipped it, and their reserve stayed where the reset drew it."""
        if self.fatigue_on:
            spent = (tau * jv).abs().sum(-1) / self.fatigue_j
            self.reserve = (self.reserve + self.dt * ((1.0 - self.reserve) / self.fatigue_rec_s - spent)).clamp(0.0, 1.0)

    def _tired(self) -> torch.Tensor:
        """Share of drive strength a tiring body has: all of it down to half its reserve, then less."""
        return 1.0 - self.fatigue_weak * (1.0 - self.reserve / 0.5).clamp(0.0, 1.0)

    def _hurt01(self) -> torch.Tensor:
        """How much of hurt_full a fighter has taken, 0 to 1 (0 where the rule is off)."""
        return torch.where(self.hurt_full > 0.0, self.hurt / self.hurt_full.clamp_min(1e-6), torch.zeros_like(self.hurt)).clamp(0.0, 1.0)
