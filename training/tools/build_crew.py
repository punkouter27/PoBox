"""The ring's people who are not boxers, as bodies to train: the referee and the ring-card walker (2026-11-03, owner:
"use rl training for ringcard girl and referee"). Their meshes are the stylised figures in Assets/Art/Exports/GLB
(BLENDER/2026-11-03_figures/build_figures.py); glb_to_rig.py read rigs/crew/NAME.json off them.

    python tools/build_crew.py                  rigs/crew, models/crew, the checks
    python tools/build_crew.py --only referee

Built as models/v3 is (tools/build_v3.py): human force each way, per-joint action scale, the trunk's bend the right
way round, de Leva's segment masses (the walker's a woman's), damping measured standing, and the boxers' rules of a
living body (the speed limit and fatigue, from Nick's v3 config). Two differences: no punch rules matter to them, and
the pose a zero action asks for is their own (rig "stance_deg"): the referee's arms hang at his sides; the walker's
left arm hangs and her right holds the card up beside her head. Checked for self-contact like every boxer.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "tools"))
import rig_to_mjcf  # noqa: E402
from build_v3 import DE_LEVA, TRUNK_RANGE, save, with_rules  # noqa: E402
from check_self_collision import check  # noqa: E402

# Arms hanging: the left arm's numbers, mirrored for the right (rig_to_mjcf.GUARD_DEG's convention).
ARMS_DOWN = {"shoulder_x": -84, "shoulder_z": -8, "elbow": -12}
CREW = {
    "referee": {"sex": "M", "stance": dict(ARMS_DOWN)},
    # The right arm's own numbers (not mirrored): the upper arm out and turned up, the forearm up beside the head, the
    # card in the hand about 20 cm over it (the highest the shoulder's limits let a hand go, found on the v3 bodies).
    "cardwalker": {"sex": "F", "stance": dict(ARMS_DOWN, shoulder_x_r=86.5, shoulder_z_r=100.0, elbow_r=69.0)},
}
RULES_FROM = "nick"


def mass_frac(sex: str) -> dict:
    t = DE_LEVA[sex]
    whole = t["pelvis"] + t["torso"] + t["head"] + 2 * sum(t[k] for k in ("upper_arm", "forearm", "hand", "thigh", "shin", "foot"))
    return {k: round(v / whole, 5) for k, v in t.items()}


def build(name: str, rigs: str, models: str) -> None:
    spec = CREW[name]
    rules = json.load(open(os.path.join(HERE, "models", "v3", f"{RULES_FROM}_policy_config.json"), encoding="utf-8"))
    rig_path = os.path.join(rigs, f"{name}.json")
    rig = json.load(open(rig_path, encoding="utf-8"))
    rig["human_force"] = True
    rig["per_joint_action"] = True
    rig.pop("joint_kv", None)
    rig["ranges_deg"] = TRUNK_RANGE
    rig["segment_mass_frac"] = mass_frac(spec["sex"])
    rig["stance_deg"] = spec["stance"]
    save(rig_path, rig)

    z, cfg = rig_to_mjcf.build_one(rig, rig_path, name, models, 0)
    save(os.path.join(models, f"{name}_policy_config.json"), with_rules(cfg, rules))
    env = dict(os.environ, PYTHONPATH=os.path.join(HERE, ".mj350"))
    subprocess.run([sys.executable, os.path.join(HERE, "tools", "pd_sweep.py"), "--name", name, "--loaded",
                    "--models", os.path.relpath(models, HERE), "--rig", rig_path,
                    "--write", os.path.join("logs", f"pd_crew_{name}.json")], cwd=HERE, env=env, check=True, stdout=subprocess.DEVNULL)
    rig = json.load(open(rig_path, encoding="utf-8"))
    z, cfg = rig_to_mjcf.build_one(rig, rig_path, name, models, 0)
    save(os.path.join(models, f"{name}_policy_config.json"), with_rules(cfg, rules))
    if check(name, models) != 0:
        raise SystemExit(f"{name}: its own parts touch; see the list above")
    print(f"{name}: {cfg.get('total_mass_kg', '?')} kg, pelvis {z:.3f} m standing")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", action="append", default=[])
    ap.add_argument("--models", default=os.path.join(HERE, "models", "crew"))
    ap.add_argument("--rigs", default=os.path.join(HERE, "rigs", "crew"))
    a = ap.parse_args()
    os.makedirs(a.models, exist_ok=True)
    for name in a.only or list(CREW):
        build(name, a.rigs, a.models)


if __name__ == "__main__":
    main()
