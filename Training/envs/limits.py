"""The numbers behind the 2026-11-25 progressive-training upgrades, torch-free: physical limits, terrain, navigation
and turning.

These are the reference formulas; envs/constraints.py, envs/terrain.py, envs/nav.py and envs/momentum.py apply them to
whole batches of worlds on the GPU, and tests/test_rl_progression.py checks the maths here without a trainer.

Physical limits (a body that is not a servomotor, AGENTS.md 2.3):

    gravity      each world feels g x (1 + s), s uniform in +-gravity; the observation's "which way is down" is that g
    torque cap   the motor may use only `share` of the drive's force limit: the target is moved toward where the joint
                 is until kp (target - q) fits inside the cap
    motor lag    a first-order lag on the target, bandwidth `hz`: alpha = 1 - exp(-2 pi hz dt); 0 Hz = no lag
    soft stop    inside the last `soft_limit` radians of a joint's range the target gives way to the stop, so the drive
                 stops pushing before the hard limit does
"""

from __future__ import annotations

import json
import math
import os
from dataclasses import dataclass, fields

PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "config", "progression.json")
G = 9.81


def load(path: str = PATH) -> dict:
    """The progression config, its "_doc" dropped."""
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as fh:
        return {k: v for k, v in json.load(fh).items() if not k.startswith("_")}


def _over(out, section: dict, where: str) -> None:
    """A config section's keys over a dataclass, refusing keys the dataclass does not have."""
    known = {f.name for f in fields(out)}
    unknown = set(section) - known
    if unknown:
        raise ValueError(f"{where}: unknown key(s) {sorted(unknown)}; known: {sorted(known)}")
    for k, v in section.items():
        if v is not None:
            setattr(out, k, type(getattr(out, k))(v))


# ---- physical limits -------------------------------------------------------------------------------
@dataclass
class PhysRanges:
    gravity: float = 0.0  # +- share of g, drawn per world (0 = Earth exactly)
    torque_cap: float = 1.0  # share of the drive's force limit the motor may use (1 = no cap)
    torque_var: float = 0.0  # +- share of the cap, drawn per fighter
    act_lag_hz: float = 0.0  # first-order motor bandwidth, Hz (0 = the drive follows at once)
    lag_var: float = 0.0  # +- share of the bandwidth, drawn per fighter
    soft_limit: float = 0.0  # rad inside each joint's range where the stop starts to give (0 = hard limit)
    rom_w: float = 0.0  # charge per radian pressed into the stop, per step

    @classmethod
    def from_config(cls, path: str = PATH, **over) -> "PhysRanges":
        out = cls()
        _over(out, load(path).get("physics", {}), path)
        _over(out, {k: v for k, v in over.items() if v is not None}, "physics (command line)")
        if out.gravity < 0.0 or out.gravity >= 1.0:
            raise ValueError("gravity is a +- share of g: 0 <= gravity < 1")
        if not 0.0 < out.torque_cap <= 1.0 or not 0.0 <= out.torque_var < 1.0:
            raise ValueError("torque_cap is a share of the force limit in (0, 1]; torque_var in [0, 1)")
        if out.act_lag_hz < 0.0 or not 0.0 <= out.lag_var < 1.0:
            raise ValueError("act_lag_hz >= 0; lag_var in [0, 1)")
        if out.soft_limit < 0.0 or out.rom_w < 0.0:
            raise ValueError("soft_limit and rom_w are >= 0")
        return out

    @property
    def any(self) -> bool:
        """Whether any limit is stricter than the game's own body."""
        return bool(
            self.gravity or self.torque_cap < 1.0 or self.torque_var or self.act_lag_hz or self.soft_limit or self.rom_w
        )


def lag_alpha(hz: float, dt: float) -> float:
    """Share of the gap a first-order motor closes in one control step."""
    if hz <= 0.0:
        return 1.0
    return 1.0 - math.exp(-2.0 * math.pi * hz * dt)


def cap_target(want: float, q: float, kp: float, cap: float) -> float:
    """The target that asks the drive for at most `cap` newtons either way, from joint position q."""
    if kp <= 0.0 or cap <= 0.0:
        return q
    torque = kp * (want - q)
    return q + max(-cap, min(cap, torque)) / kp


def stop_gain(q: float, lo: float, hi: float, margin: float) -> float:
    """0 outside the last `margin` radians of a joint's range, 1 at the limit."""
    if margin <= 0.0:
        return 0.0
    return max(0.0, min(1.0, max((q - (hi - margin)) / margin, ((lo + margin) - q) / margin)))


def stop_target(want: float, q: float, lo: float, hi: float, margin: float) -> float:
    """The target with the compliant stop folded in: at the limit it is the limit, so the drive stops pushing."""
    g = stop_gain(q, lo, hi, margin)
    if g <= 0.0:
        return want
    limit = hi if (hi - q) < (q - lo) else lo
    return want * (1.0 - g) + limit * g


# ---- terrain ---------------------------------------------------------------------------------------
@dataclass
class TerrainClass:
    name: str = "flat"
    weight: float = 1.0
    slope_deg: tuple = (0.0, 0.0)  # the tilt drawn inside this class, degrees
    friction: float = 1.0  # times the floor's contact friction
    bump_hz: float = 0.0  # uneven-ground impulses a fighter a second


@dataclass
class TerrainRanges:
    bump_mps: float = 0.15  # the size of an uneven-ground impulse, m/s of the body's root
    classes: tuple = ()

    @classmethod
    def from_config(cls, path: str = PATH, classes: str = "", **over) -> "TerrainRanges":
        out = cls()
        raw = load(path).get("terrain", {})
        out.bump_mps = float(raw.get("bump_mps", out.bump_mps))
        given = raw.get("classes", [])
        if classes and classes.strip().lower() != "all":  # a comma list of names keeps only those classes
            want = {c.strip() for c in classes.split(",") if c.strip()}
            unknown = want - {c["name"] for c in given}
            if unknown:
                raise ValueError(f"--terrain: no class {sorted(unknown)}; have {[c['name'] for c in given]}")
            given = [c for c in given if c["name"] in want]
        out.classes = tuple(
            TerrainClass(
                name=str(c["name"]),
                weight=float(c.get("weight", 1.0)),
                slope_deg=tuple(float(v) for v in c.get("slope_deg", (0.0, 0.0))),
                friction=float(c.get("friction", 1.0)),
                bump_hz=float(c.get("bump_hz", 0.0)),
            )
            for c in given
        )
        if not out.classes:
            raise ValueError(f"{path}: terrain.classes is empty; the terrain rung needs at least one class")
        if any(c.weight < 0.0 for c in out.classes) or sum(c.weight for c in out.classes) <= 0.0:
            raise ValueError("terrain class weights are >= 0 and have a positive sum")
        if out.bump_mps < 0.0:
            raise ValueError("terrain bump_mps >= 0")
        return out


def pick_class(weights, u: float) -> int:
    """The class a draw u in [0, 1) falls in, by weight."""
    total = float(sum(weights))
    acc = 0.0
    for i, w in enumerate(weights):
        acc += float(w) / total
        if u < acc:
            return i
    return len(weights) - 1


def tilt_vector(slope_deg: float, way_rad: float) -> tuple:
    """The gravity direction of a floor tilted `slope_deg` up toward `way_rad` (unit vector, z up)."""
    t = math.radians(slope_deg)
    return (math.sin(t) * math.cos(way_rad), math.sin(t) * math.sin(way_rad), -math.cos(t))


# ---- navigation ------------------------------------------------------------------------------------
@dataclass
class NavRanges:
    reward: float = 1.0  # paid per m/s of progress toward the goal
    arrive: float = 8.0  # paid once, on arrival
    obstacles: tuple = (1, 3)  # least and most obstacles parked across the path
    reach_m: float = 0.35  # how near the goal counts as arrived

    @classmethod
    def from_config(cls, path: str = PATH, **over) -> "NavRanges":
        out = cls()
        _over(out, load(path).get("nav", {}), path)
        _over(out, over, "nav (command line)")
        if out.reward < 0.0 or out.arrive < 0.0 or out.reach_m <= 0.0:
            raise ValueError("nav reward and arrive are >= 0; reach_m > 0")
        lo, hi = (int(v) for v in out.obstacles)
        if lo < 0 or hi < lo:
            raise ValueError("nav obstacles is [least, most] with 0 <= least <= most")
        out.obstacles = (lo, hi)
        return out


def steer(dx: float, dy: float, yaw: float, fwd_max: float, side_max: float = 0.4, turn_gain: float = 2.0) -> tuple:
    """The (forward, side, turn) command that heads for a goal `dx, dy` away, in the fighter's own heading frame."""
    c, s = math.cos(yaw), math.sin(yaw)
    fwd = dx * c + dy * s
    side = -dx * s + dy * c
    turn = max(-1.0, min(1.0, turn_gain * math.atan2(side, max(fwd, 1e-3))))
    return (max(-0.4, min(fwd_max, fwd)), max(-side_max, min(side_max, side * 0.8)), turn)


# ---- momentum-aware turning ------------------------------------------------------------------------
def yaw_rate_step(rate: float, target: float, accel: float, dt: float) -> float:
    """A commanded yaw rate that can only change by `accel` rad/s^2: the body has mass, so must the command."""
    step = accel * dt
    return rate + max(-step, min(step, target - rate))


def lean_offset(v_fwd: float, rate: float, height: float, g: float = G) -> float:
    """The lateral pelvis offset a turn needs, metres: the centripetal lean v w / g, applied at the pelvis height."""
    return height * (v_fwd * rate) / g
