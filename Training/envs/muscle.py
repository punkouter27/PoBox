"""A Hill-type muscle on each PD joint drive (train_box.py --muscle hill; off by default).

The game's MjBoxer.Drive runs the same rule from a boxer config's "muscle" block (Assets/_Project/Core/Scripts/Mj/
HillMuscle.cs, number for number), so a brain trained under it plays under it. Once a control step, for each joint:

    excitation   u = clamp(kp (target - q) / (F peak), -1, 1)        what the policy's PD target asks of the muscle
    activation   a += (u - a) (1 - exp(-dt / (tau_s tau_scale)))      first-order dynamics, ~30 ms
    length       fL = exp(-(x / fl_width)^2), x = (2q - lo - hi) / (hi - lo)   weaker towards the joint's ends
    velocity     v = sign(a) qdot / vmax; shortening (v >= 0): fV = max(0, (1 - v) / (1 + v / fv_k)), nothing at the
                 speed limit; lengthening: w = min(-v, 1), fV = fv_ecc - (fv_ecc - 1)(1 - w) / (1 + w / fv_k)
    torque       T = a F peak fL fV scale                               scale = daze x fatigue, as the drives had
    target       q + T / kp                                             the drive's damping (kv qdot) is untouched

F is the config's force_limit and vmax its velocity_limit for the joint. fV is the muscle rule of AGENTS.md 2.3 (torque
falls to nothing at the joint's speed limit), so a fighter under the Hill muscle does not also get the linear speed
limit's extra damping (force_limit / velocity_limit). Fatigue (the reserve) multiplies T. `peak` and `tau_scale` are
per boxer: an old body (Grandma, Grandpa) is a lower peak and a slower activation. Activation starts at 0 each episode.
"""

from __future__ import annotations

import math
from dataclasses import asdict, dataclass, fields

import torch

MODEL = "hill"


@dataclass
class MuscleParams:
    peak: float = 1.0  # share of the config's force_limit the muscle can give at best (age)
    tau_s: float = 0.03  # activation time constant, seconds
    tau_scale: float = 1.0  # times tau_s (age)
    fl_width: float = 1.6  # force-length Gaussian width over the joint range's half (1.6: 68% at the ends)
    fv_k: float = 0.25  # Hill's curvature (a/F0)
    fv_ecc: float = 1.3  # force while lengthening fast, as a share of isometric

    def block(self) -> dict:
        """The config's "muscle" block, as the game reads it (BoxerConfig.muscle)."""
        return {"model": MODEL, **asdict(self)}


def params_from(block: dict | None, **over) -> MuscleParams:
    """A boxer config's muscle block over the defaults, then any keyword that is not None over those."""
    p = MuscleParams()
    for f in fields(MuscleParams):
        if block and f.name in block:
            setattr(p, f.name, float(block[f.name]))
        if over.get(f.name) is not None:
            setattr(p, f.name, float(over[f.name]))
    if p.peak <= 0.0 or p.tau_s <= 0.0 or p.tau_scale <= 0.0 or p.fl_width <= 0.0 or p.fv_k <= 0.0:
        raise ValueError(f"muscle parameters must be positive: {asdict(p)}")
    if p.fv_ecc < 1.0:
        raise ValueError("fv_ecc is the lengthening force over isometric: >= 1")
    return p


def is_hill(cfg: dict) -> bool:
    return str((cfg.get("muscle") or {}).get("model", "")) == MODEL


# ---- the curves, on tensors (any broadcastable shapes) --------------------------------------------
def force_length(q, lo, hi, width):
    x = (2.0 * q - lo - hi) / (hi - lo).clamp_min(1e-6)
    return torch.exp(-((x / width) ** 2))


def force_velocity(v, k, ecc):
    """v: joint speed along the torque, over the speed limit (positive = shortening)."""
    con = ((1.0 - v) / (1.0 + v / k)).clamp_min(0.0)
    w = (-v).clamp(0.0, 1.0)
    lng = ecc - (ecc - 1.0) * (1.0 - w) / (1.0 + w / k)
    return torch.where(v >= 0.0, con, lng)


class HillDrive:
    """The muscles of every fighter of a batched env: per-fighter parameters (K,), per-joint limits (K, A), and the
    activation state (N, K, A)."""

    def __init__(self, params: list[MuscleParams], force: torch.Tensor, vmax: torch.Tensor, lo, hi, n: int):
        dev = force.device
        P = lambda key: torch.tensor([getattr(p, key) for p in params], device=dev, dtype=torch.float32).view(1, -1, 1)
        self.peak, self.fl_width, self.fv_k, self.fv_ecc = P("peak"), P("fl_width"), P("fv_k"), P("fv_ecc")
        self.tau = P("tau_s") * P("tau_scale")
        self.force, self.vmax, self.lo, self.hi = force[None], vmax[None].clamp_min(1e-3), lo[None], hi[None]
        self.act = torch.zeros(n, len(params), force.shape[-1], device=dev)

    def reset(self, mask: torch.Tensor) -> None:
        self.act.copy_(torch.where(mask[:, None, None], torch.zeros_like(self.act), self.act))

    def target(self, want, q, qd, kp, scale, dt: float) -> torch.Tensor:
        """The PD target (N, K, A) that makes the drive's spring give the muscle's torque. scale: (N, K)."""
        top = self.force * self.peak
        u = (kp * (want - q) / top).clamp(-1.0, 1.0)
        alpha = 1.0 - torch.exp(-dt / self.tau)
        self.act.copy_(self.act + alpha * (u - self.act))  # in place: envs/step_graph.py pins only the env's own
        v = torch.sign(self.act) * qd / self.vmax
        fl = force_length(q, self.lo, self.hi, self.fl_width)
        fv = force_velocity(v, self.fv_k, self.fv_ecc)
        torque = self.act * top * fl * fv * scale[..., None]
        return q + torque / kp


def hill_target_scalar(
    want: float,
    q: float,
    qd: float,
    a: float,
    kp: float,
    force: float,
    vmax: float,
    lo: float,
    hi: float,
    scale: float,
    dt: float,
    p: MuscleParams,
) -> tuple[float, float]:
    """One joint, plain floats: (target, new activation). The reference the game's HillMuscle.cs is checked against."""
    top = force * p.peak
    u = max(-1.0, min(1.0, kp * (want - q) / top))
    a = a + (u - a) * (1.0 - math.exp(-dt / (p.tau_s * p.tau_scale)))
    sgn = 1.0 if a > 0.0 else -1.0 if a < 0.0 else 0.0
    v = sgn * qd / max(vmax, 1e-3)
    x = (2.0 * q - lo - hi) / max(hi - lo, 1e-6)
    fl = math.exp(-((x / p.fl_width) ** 2))
    if v >= 0.0:
        fv = max(0.0, (1.0 - v) / (1.0 + v / p.fv_k))
    else:
        w = min(-v, 1.0)
        fv = p.fv_ecc - (p.fv_ecc - 1.0) * (1.0 - w) / (1.0 + w / p.fv_k)
    return q + a * top * fl * fv * scale / kp, a
