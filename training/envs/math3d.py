"""Quaternion / heading helpers and punch-variety accounting shared across boxing envs."""
from __future__ import annotations

import torch

# The mass behind a glove: a punch's impulse is this times its closing speed (the game's Fighter.PunchMass).
PUNCH_MASS = 2.2
# The fatigue a new boxer trains under when its config does not say (default_rules): every shipped boxer's config has 20000.
DEFAULT_FATIGUE_J = 20000.0


def quat_rotate_inverse(q: torch.Tensor, v: torch.Tensor) -> torch.Tensor:
    """q: (..., 4) wxyz, v: (..., 3) world -> body."""
    w, xyz = q[..., :1], q[..., 1:]
    t = 2.0 * torch.cross(xyz, v, dim=-1)
    return v - w * t + torch.cross(xyz, t, dim=-1)


def quat_yaw(q: torch.Tensor) -> torch.Tensor:
    w, x, y, z = q[..., 0], q[..., 1], q[..., 2], q[..., 3]
    return torch.atan2(2.0 * (w * z + x * y), 1.0 - 2.0 * (y * y + z * z))


def yaw_quat(yaw: torch.Tensor) -> torch.Tensor:
    z = torch.zeros_like(yaw)
    return torch.stack([torch.cos(yaw * 0.5), z, z, torch.sin(yaw * 0.5)], dim=-1)


def quat_mul(a: torch.Tensor, b: torch.Tensor) -> torch.Tensor:
    aw, ax, ay, az = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    bw, bx, by, bz = b[..., 0], b[..., 1], b[..., 2], b[..., 3]
    return torch.stack([aw * bw - ax * bx - ay * by - az * bz, aw * bx + ax * bw + ay * bz - az * by,
                        aw * by - ax * bz + ay * bw + az * bx, aw * bz + ax * by - ay * bx + az * bw], -1)


def to_heading(v: torch.Tensor, yaw: torch.Tensor) -> torch.Tensor:
    """Rotate world vectors (..., 3) into the frame that has turned `yaw` about the vertical."""
    c, s = torch.cos(yaw), torch.sin(yaw)
    return torch.stack([c * v[..., 0] + s * v[..., 1], -s * v[..., 0] + c * v[..., 1], v[..., 2]], dim=-1)


def punch_variety(landed, arriving, zone, last_kind, streak, since_hit, combo_s: float, pause_s: float = 2.0):
    """Which punch each fighter landed this step and how it follows the last one. landed, arriving, zone: (N,K,2), per
    glove; last_kind: (N,K) hand x 2 + zone of the last punch landed (-1 none); streak: how many in a row before this were
    that punch; since_hit: seconds since the last one. A punch is its hand and where it went (the faster glove's if both
    land); a pause of pause_s starts the count afresh; a combination is the other hand within combo_s.
    Returns any_land, same (this one repeats the last), combo, combo_q (its speed, m/s), the new streak and last_kind."""
    any_land = landed.any(-1)
    hand = (landed.float() * arriving).argmax(-1)
    kind = hand * 2 + zone.gather(-1, hand[..., None]).squeeze(-1)
    same = any_land & (kind == last_kind) & (since_hit <= pause_s)
    streak = torch.where(any_land, torch.where(same, streak + 1.0, torch.zeros_like(streak)), streak)
    combo = any_land & (last_kind >= 0) & (hand != torch.div(last_kind, 2, rounding_mode="floor")) & (since_hit < combo_s)
    combo_q = combo.float() * (landed.float() * arriving).amax(-1)
    return any_land, same, combo, combo_q, streak, torch.where(any_land, kind, last_kind)
