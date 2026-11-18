play s"""How fast can a boxer's glove go at all? A full-effort probe of the arm, in C MuJoCo, no brain (2026-11-16).

    PYTHONPATH=.mj350 python tools/fist_speed.py --names matt kim trump grandma grandpa

The boxer's own model (models/v3/NAME_solo.xml) with the pelvis held in the air (as tools/pd_sweep.py does) and the
speed-limit damping of its config (force limit / speed limit, as the trainer adds it). From the guard, the right arm's
three joints (shoulder_x_r, shoulder_z_r, elbow_r) are driven at every combination of -clip, 0 and +clip actions
(action * action_scale_j radians off the guard, the trainer's action clip), each for --seconds. The glove's peak speed
relative to the fixed pelvis is taken over the whole try, and the best try is the ceiling the brain could reach with this
arm alone (no trunk turn, no step: a real punch adds those, so a brain can beat this, but not by a factor of several).
The exam's attack mark is 3.0 m/s for the old boxers and 4.0-4.4 for the rest; a hit counts above 1 m/s closing.
Fatigue starts full, so its ceiling does not bite. It changes no model.
"""
from __future__ import annotations

import argparse
import itertools
import json
import os

import mujoco
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ARM = ["shoulder_x_r", "shoulder_z_r", "elbow_r"]


def probe(name: str, models: str, seconds: float, levels: list[float]) -> dict:
    m = mujoco.MjModel.from_xml_path(os.path.join(HERE, models, f"{name}_solo.xml"))
    cfg = json.load(open(os.path.join(HERE, models, f"{name}_policy_config.json"), encoding="utf-8"))
    order = cfg["joint_order"]
    jid = [mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_JOINT, "a_" + n) for n in order]
    if cfg.get("speed_limit", False):
        for j, fl, vl in zip(jid, cfg["force_limit"], cfg["velocity_limit"]):
            m.dof_damping[m.jnt_dofadr[j]] += fl / vl
    root_q = int(m.jnt_qposadr[mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_JOINT, "a_root")])
    root_v = int(m.jnt_dofadr[mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_JOINT, "a_root")])
    glove = mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_GEOM, "a_glove_r")
    d = mujoco.MjData(m)
    mujoco.mj_resetDataKeyframe(m, d, 0)
    d.qpos[root_q + 2] += 0.5
    base = d.qpos[root_q:root_q + 7].copy()
    guard = np.array([d.qpos[m.jnt_qposadr[j]] for j in jid])
    scale = np.array(cfg.get("action_scale_j", [cfg["action_scale"]] * len(order)))
    clip = float(cfg.get("action_clip", 3.0))
    lo = np.array([m.jnt_range[j][0] for j in jid])
    hi = np.array([m.jnt_range[j][1] for j in jid])
    start = d.qpos.copy()
    idx = [order.index(n) for n in ARM]
    v6 = np.zeros(6)

    def run(target: np.ndarray, secs: float) -> float:
        peak = 0.0
        for _ in range(int(secs / m.opt.timestep)):
            d.ctrl[:] = target
            mujoco.mj_step(m, d)
            d.qpos[root_q:root_q + 7] = base
            d.qvel[root_v:root_v + 6] = 0.0
            mujoco.mj_forward(m, d)
            mujoco.mj_objectVelocity(m, d, mujoco.mjtObj.mjOBJ_GEOM, glove, v6, 0)
            peak = max(peak, float(np.linalg.norm(v6[3:])))
        return peak

    best = (0.0, None)
    for combo in itertools.product(levels, repeat=len(ARM)):
        if not any(combo):
            continue
        d.qpos[:], d.qvel[:] = start, 0.0
        run(guard, 0.3)
        target = guard.copy()
        for i, a in zip(idx, combo):
            target[i] = np.clip(guard[i] + a * clip * scale[i], lo[i], hi[i])
        v = run(target, seconds)
        if v > best[0]:
            best = (v, combo)
    arm_force = [cfg["force_limit"][i] for i in idx]
    return {"name": name, "peak_glove_m_s": round(best[0], 2), "best_actions": best[1], "strength": cfg.get("strength"),
            "speed": cfg.get("speed"), "arm_force_limits": [round(f, 1) for f in arm_force],
            "arm_speed_limits": [round(cfg["velocity_limit"][i], 1) for i in idx]}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--names", nargs="+", default=["matt", "kim", "trump", "grandma", "grandpa"])
    ap.add_argument("--models", default=os.path.join("models", "v3"))
    ap.add_argument("--seconds", type=float, default=0.4)
    ap.add_argument("--json", default="")
    args = ap.parse_args()
    levels = [-1.0, 0.0, 1.0]
    rows = []
    print(f"mujoco {mujoco.__version__}; right arm, full effort, pelvis fixed")
    for n in args.names:
        r = probe(n, args.models, args.seconds, levels)
        rows.append(r)
        print(f"{n:8} peak glove {r['peak_glove_m_s']:5.2f} m/s  actions {r['best_actions']}  strength {r['strength']}  "
              f"speed {r['speed']}  force {r['arm_force_limits']}  speed limits {r['arm_speed_limits']}")
    if args.json:
        json.dump(rows, open(os.path.join(HERE, args.json), "w"), indent=1)


if __name__ == "__main__":
    main()
