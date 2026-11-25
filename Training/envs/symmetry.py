"""Bilateral (left/right sagittal) mirror maps for PoBox observations and actions.

Every PoBox fighter shares the same 21-joint humanoid layout (abdomen_z/y/x, left arm, right arm,
left leg, right leg) and heading-frame observation vector (100 base dims, +3 command/state dims
in Footwork/Match, +3 nearest-body dims in Melee). Mirroring across the sagittal plane (Y -> -Y)
is an exact involution (M(M(x)) == x) that lets PPO enforce left/right policy symmetry without
changing the exported ONNX graph or Unity inference.
"""

from __future__ import annotations

from typing import Sequence, Tuple

import torch

# Sagittal mirror sign for a joint whose left/right pair is swapped (or a midline joint).
# In PoBox's MuJoCo rigs (Assets/_Project/Agents/Boxers/*/config.json):
#   - abdomen_z (yaw) and abdomen_x (roll) flip sign; abdomen_y (pitch) keeps sign.
#   - shoulder_x, shoulder_z, elbow have opposite signs between _l and _r (guard poses negate).
#   - hip_x (roll), hip_z (yaw), ankle_x (roll) flip sign; hip_y, knee, ankle_y (pitch) keep sign.
_FLIP_PREFIXES = ("abdomen_z", "abdomen_x", "shoulder_x", "shoulder_z", "elbow", "hip_x", "hip_z", "ankle_x")


def _joint_mirror(joint_order: Sequence[str]) -> Tuple[list[int], list[float]]:
    idx_of = {name: i for i, name in enumerate(joint_order)}
    perm: list[int] = []
    sign: list[float] = []
    for i, name in enumerate(joint_order):
        if name.endswith("_l"):
            mirror_name = name[:-2] + "_r"
            target = idx_of[mirror_name]
        elif name.endswith("_r"):
            mirror_name = name[:-2] + "_l"
            target = idx_of[mirror_name]
        else:
            target = i
        perm.append(target)
        s = -1.0 if any(name.startswith(p) for p in _FLIP_PREFIXES) else 1.0
        sign.append(s)
    return perm, sign


def build_mirror_maps(
    joint_order: Sequence[str],
    obs_dim: int,
    device: str = "cpu",
    obs_state_tail: bool = False,
) -> Tuple[torch.Tensor, torch.Tensor, torch.Tensor, torch.Tensor]:
    """Build (obs_perm, obs_sign, act_perm, act_sign) tensors for sagittal mirroring.

    Args:
        joint_order: 21-joint names in action/qpos order.
        obs_dim: Total observation width (100, 103, or 106).
        device: Target torch device.
        obs_state_tail: True when dims 100..103 hold scalar (reserve, own_daze, opp_daze)
            instead of velocity commands (vx, vy, yaw_rate).
    """
    a_perm, a_sign = _joint_mirror(joint_order)
    A = len(a_perm)
    base_dim = 9 + 3 * A + 2 + 1 + 9 + 12 + 2 + 2  # 100 for A=21
    if obs_dim < base_dim:
        raise ValueError(f"obs_dim={obs_dim} is smaller than base boxing obs ({base_dim})")

    o_perm: list[int] = []
    o_sign: list[float] = []

    def add_vec3(start: int, signs: Tuple[float, float, float]) -> None:
        o_perm.extend([start, start + 1, start + 2])
        o_sign.extend(signs)

    def add_joints(start: int) -> None:
        o_perm.extend(start + p for p in a_perm)
        o_sign.extend(a_sign)

    # 0..9: base_lin_vel(3), base_ang_vel(3), projected_gravity(3)
    add_vec3(0, (1.0, -1.0, 1.0))
    add_vec3(3, (-1.0, 1.0, -1.0))
    add_vec3(6, (1.0, -1.0, 1.0))

    # 9..9+3A: jp(A), jv(A), last_action(A)
    add_joints(9)
    add_joints(9 + A)
    add_joints(9 + 2 * A)

    # foot_contact(2: L, R) -> swap L/R; base_height(1)
    c0 = 9 + 3 * A
    o_perm.extend([c0 + 1, c0, c0 + 2])
    o_sign.extend([1.0, 1.0, 1.0])

    # target_head(3), target_body(3), target_head_vel(3)
    t0 = c0 + 3
    add_vec3(t0, (1.0, -1.0, 1.0))
    add_vec3(t0 + 3, (1.0, -1.0, 1.0))
    add_vec3(t0 + 6, (1.0, -1.0, 1.0))

    # own_gloves(2x3: L, R) -> swap L/R and negate Y
    g0 = t0 + 9
    o_perm.extend([g0 + 3, g0 + 4, g0 + 5, g0, g0 + 1, g0 + 2])
    o_sign.extend([1.0, -1.0, 1.0, 1.0, -1.0, 1.0])

    # opp_gloves(2x3: L, R) -> swap L/R and negate Y
    og0 = g0 + 6
    o_perm.extend([og0 + 3, og0 + 4, og0 + 5, og0, og0 + 1, og0 + 2])
    o_sign.extend([1.0, -1.0, 1.0, 1.0, -1.0, 1.0])

    # opponent_facing(cos, sin) -> (+1, -1); position_in_ring(x, y) -> (+1, -1)
    f0 = og0 + 6
    o_perm.extend([f0, f0 + 1, f0 + 2, f0 + 3])
    o_sign.extend([1.0, -1.0, 1.0, -1.0])

    # Optional tail: +3 command/state dims, +3 nearest downed body dims
    tail = obs_dim - base_dim
    if tail >= 3:
        o_perm.extend([base_dim, base_dim + 1, base_dim + 2])
        if obs_state_tail:
            o_sign.extend([1.0, 1.0, 1.0])
        else:
            o_sign.extend([1.0, -1.0, -1.0])
    if tail >= 6:
        b0 = base_dim + 3
        o_perm.extend([b0, b0 + 1, b0 + 2])
        o_sign.extend([1.0, -1.0, 1.0])
    for extra in range(len(o_perm), obs_dim):
        o_perm.append(extra)
        o_sign.append(1.0)

    return (
        torch.tensor(o_perm, dtype=torch.long, device=device),
        torch.tensor(o_sign, dtype=torch.float32, device=device),
        torch.tensor(a_perm, dtype=torch.long, device=device),
        torch.tensor(a_sign, dtype=torch.float32, device=device),
    )
