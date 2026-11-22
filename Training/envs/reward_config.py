"""The reward penalty weights of the training config (Training/config/reward.json), one set per environment.

Every env charges, per control step and each with its own weight: actuator torque squared (``torque_sq``), joint
speed squared (``joint_vel_sq``), pelvis roll/pitch angular velocity squared (``ang_vel_sq``), the squared action
change (``action_rate``) and foot speed squared while the foot is down (``foot_slip``). A weight left out of the file
keeps the default below, which is what the env charged before the file existed (the two newer terms default to 0).
"""

from __future__ import annotations

import json
import os

PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "config", "reward.json")
KEYS = ("torque_sq", "joint_vel_sq", "ang_vel_sq", "action_rate", "foot_slip")
DEFAULTS = {
    "boxing": {"torque_sq": 0.0, "joint_vel_sq": 0.0, "ang_vel_sq": 0.02, "action_rate": 0.01, "foot_slip": 0.2},
    "footwork": {"torque_sq": 0.0, "joint_vel_sq": 0.0, "ang_vel_sq": 0.02, "action_rate": 0.01, "foot_slip": 0.2},
    "getup": {"torque_sq": 0.0, "joint_vel_sq": 0.0, "ang_vel_sq": 0.0, "action_rate": 0.005, "foot_slip": 0.1},
}


def penalties(env: str, path: str = PATH) -> dict[str, float]:
    """The weights for one env ("boxing", "footwork" or "getup"), the file's over the defaults."""
    weights = dict(DEFAULTS[env])
    if os.path.exists(path):
        with open(path, encoding="utf-8") as fh:
            given = json.load(fh).get(env, {})
        unknown = set(given) - set(KEYS)
        if unknown:
            raise ValueError(f"{path}: unknown penalty weight(s) for {env}: {sorted(unknown)}")
        weights.update({k: float(v) for k, v in given.items()})
    return weights
