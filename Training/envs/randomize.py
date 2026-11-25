"""Per-episode domain randomisation for the batched Warp envs (train_box.py --domain-rand; off by default).

At every episode reset each world draws: one ground/contact friction factor, a mass factor per body (inertia scaled with
it), one PD stiffness (kp) and one damping (kd) factor for its drives, a physics timestep factor (control-timestep
jitter: the policy still acts every 4 substeps, which now last a little more or less), and per fighter an actuator delay
of 0..delay_max control steps (the drive targets are those the policy asked for that many steps ago). Extra sensor
noise is added to the observation (envs/boxing_base.py _observe). Ranges: Training/config/domain_rand.json.

MuJoCo Warp indexes every model field by world (worldid % shape[0]); a field held once is copied out to one row a
world when this is attached (before any CUDA graph is captured), and every later write is in place, so the graphs
(the env's own and envs/step_graph.py's) see it. Factors multiply the value each world had when this was attached,
which in the footwork / v2 stages is already that world's randomised body (FootworkEnv._randomise).
"""

from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass, fields

import numpy as np
import torch
import warp as wp

PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "config", "domain_rand.json")
PRIVILEGED = 6  # friction, mean mass, kp, kd (each factor - 1), delay / delay_max, dt factor - 1


@dataclass
class RandRanges:
    friction: float = 0.3
    mass: float = 0.1
    kp: float = 0.15
    kd: float = 0.2
    delay_max: int = 2
    dt_jitter: float = 0.05
    obs_noise: float = 0.5


def ranges(path: str = PATH, **over) -> RandRanges:
    """The config file's ranges over the defaults, then any keyword that is not None over those."""
    out = RandRanges()
    if os.path.exists(path):
        with open(path, encoding="utf-8") as fh:
            given = {k: v for k, v in json.load(fh).items() if not k.startswith("_")}
        known = {f.name for f in fields(RandRanges)}
        unknown = set(given) - known
        if unknown:
            raise ValueError(f"{path}: unknown domain randomisation range(s): {sorted(unknown)}")
        for k, v in given.items():
            setattr(out, k, type(getattr(out, k))(v))
    for k, v in over.items():
        if v is not None:
            setattr(out, k, type(getattr(out, k))(v))
    for f in fields(RandRanges):
        if getattr(out, f.name) < 0:
            raise ValueError(f"domain randomisation range {f.name} must be >= 0")
    if out.dt_jitter >= 0.5 or out.mass >= 1.0 or out.friction >= 1.0 or out.kp >= 1.0 or out.kd >= 1.0:
        raise ValueError(f"domain randomisation ranges too wide: {asdict(out)}")
    return out


def _per_world(model, name: str, n: int) -> torch.Tensor:
    """The model field as a torch view with one row a world, copying it out to n rows first if it is held once."""
    field = getattr(model, name)
    if field.shape[0] != n:
        host = np.repeat(field.numpy(), n, 0)
        setattr(model, name, wp.array(host, dtype=field.dtype, device=field.device))
    return wp.to_torch(getattr(model, name))


class DomainRand:
    def __init__(self, env, r: RandRanges):
        self.env, self.r = env, r
        N, K, A, dev = env.N, env.K, env.A, env.device
        mw = env.mw
        self.mass = _per_world(mw, "body_mass", N)  # (N, nbody)
        self.inertia = _per_world(mw, "body_inertia", N)  # (N, nbody, 3)
        self.friction = _per_world(mw, "geom_friction", N)  # (N, ngeom, 3)
        self.gain = _per_world(mw, "actuator_gainprm", N)  # (N, nu, 10)
        self.bias = _per_world(mw, "actuator_biasprm", N)
        self.timestep = _per_world(mw.opt, "timestep", N)  # (N,)
        self.base = {
            "mass": self.mass.clone(),
            "inertia": self.inertia.clone(),
            "friction": self.friction.clone(),
            "kp": self.gain[..., 0].clone(),
            "bias1": self.bias[..., 1].clone(),
            "bias2": self.bias[..., 2].clone(),
            "dt": self.timestep.clone(),
        }
        one = lambda *shape: torch.ones(*shape, device=dev)
        self.f_fric, self.f_kp, self.f_kd, self.f_dt = one(N), one(N), one(N), one(N)
        self.f_mass = one(N, self.mass.shape[1])
        self.delay = torch.zeros(N, K, dtype=torch.long, device=dev)
        self.hist = torch.zeros(int(r.delay_max) + 1, N, K, A, device=dev)
        self.obs_noise = float(r.obs_noise)

    def _u(self, *shape, span: float) -> torch.Tensor:
        e = self.env
        return 1.0 + (2.0 * torch.rand(*shape, generator=e.rng, device=e.device) - 1.0) * span

    def resample(self, mask: torch.Tensor) -> None:
        """New factors for the worlds in mask (N,), written in place into the model."""
        N, K, r, b = self.env.N, self.env.K, self.r, self.base
        m1 = mask[:, None]
        # Every state tensor here is written in place: envs/step_graph.py pins the env's own attributes, not these.
        self.f_fric.copy_(torch.where(mask, self._u(N, span=r.friction), self.f_fric))
        self.f_mass.copy_(torch.where(m1, self._u(N, self.mass.shape[1], span=r.mass), self.f_mass))
        self.f_kp.copy_(torch.where(mask, self._u(N, span=r.kp), self.f_kp))
        self.f_kd.copy_(torch.where(mask, self._u(N, span=r.kd), self.f_kd))
        self.f_dt.copy_(torch.where(mask, self._u(N, span=r.dt_jitter), self.f_dt))
        d = torch.randint(0, int(r.delay_max) + 1, (N, K), generator=self.env.rng, device=self.env.device)
        self.delay.copy_(torch.where(m1, d, self.delay))
        self.mass.copy_(b["mass"] * self.f_mass)
        self.inertia.copy_(b["inertia"] * self.f_mass[..., None])
        self.friction.copy_(b["friction"] * self.f_fric[:, None, None])
        # A position drive is gain kp and bias (0, -kp, -kv).
        self.gain[..., 0] = b["kp"] * self.f_kp[:, None]
        self.bias[..., 1] = b["bias1"] * self.f_kp[:, None]
        self.bias[..., 2] = b["bias2"] * self.f_kd[:, None]
        self.timestep.copy_(b["dt"] * self.f_dt)
        e = self.env
        if getattr(e, "_kp", None) is not None and e._kp.shape[0] == N:  # the fatigue ceiling's cached gains
            n = K * e.A
            e._kp.copy_(self.gain[:, :n, 0].reshape(e._kp.shape))
            e._kv.copy_(-self.bias[:, :n, 2].reshape(e._kv.shape))

    def delayed(self, want: torch.Tensor) -> torch.Tensor:
        """The drive targets (N, K, A) the policy asked for `delay` control steps ago; an episode's first step fills
        the history with its own targets, so nothing of the last episode comes through."""
        if self.hist.shape[0] == 1:
            return want
        fresh = (self.env.episode_len == 0.0)[None, :, None, None]
        hist = torch.cat([want[None], self.hist[:-1]], 0)
        self.hist.copy_(torch.where(fresh, want[None].expand_as(hist), hist))
        idx = self.delay[None, ..., None].expand(1, *want.shape)
        return self.hist.gather(0, idx)[0]

    def params(self) -> torch.Tensor:
        """What was drawn, for a teacher's privileged observation: (N, K, PRIVILEGED), each about 0 at nominal."""
        K = self.env.K
        world = torch.stack(
            [self.f_fric - 1.0, self.f_mass.mean(-1) - 1.0, self.f_kp - 1.0, self.f_kd - 1.0, self.f_dt - 1.0], -1
        )
        lag = self.delay.float() / max(1, int(self.r.delay_max))
        world = world[:, None].expand(-1, K, -1)
        return torch.cat([world[..., :4], lag[..., None], world[..., 4:]], -1)
