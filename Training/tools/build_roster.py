"""Build every boxer in roster.json from its mesh: the rig, the MuJoCo models, and the house-rule checks.

    python tools/build_roster.py                 every boxer, then every pair model
    python tools/build_roster.py --only nick     one boxer and the pairs it is in

For each boxer: glb_to_rig.py reads the skeleton (a "bone_map" in its entry names the bones, see
glb_to_rig.NAME_MAPS) and fits the collision shapes inside the mesh (a picture of
the fit goes to logs/fit_<name>.png); its strength, speed and style are written into the rig file;
rig_to_mjcf.py generates the bag and spar models; and tools/check_self_collision.py is run on the result.
If a part touches another in the T-pose, the guard or a normal swing, the trunk is slimmed a little and
the body is built again, until nothing does (house rule: verified before training).

Then the match models: every new boxer with every other, and with each veteran (a boxer already trained,
whose own files are left alone).
"""

from __future__ import annotations

import argparse
import itertools
import json
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "tools"))
import glb_to_rig  # noqa: E402
import rig_to_mjcf  # noqa: E402
from check_self_collision import check  # noqa: E402

REFERENCE_HEIGHT = 1.81  # the height the generator's joint strengths were written for (Matt)


def build_boxer(name: str, spec: dict, models: str, rigs: str, plots: str, cubes: int, base: str = HERE) -> tuple:
    """One boxer from its roster entry: the rig read off its mesh (glb_to_rig), its strength, speed and style, the
    models (rig_to_mjcf) and the self-collision check, slimming the trunk until nothing touches. spec["glb"] is
    relative to base (the roster's folder); spec["bone_map"], if any, is a glb_to_rig name map (a preset or a file,
    relative to base). plots empty: no picture of the fit."""
    rig_path = os.path.join(rigs, f"{name}.json")
    bone_map = spec.get("bone_map", "")
    if bone_map and bone_map.lower() not in glb_to_rig.NAME_MAPS:
        bone_map = os.path.normpath(os.path.join(base, bone_map))
    print(f"\n==== {name}: {spec['persona']}", flush=True)
    rig = glb_to_rig.rig_from_glb(
        os.path.normpath(os.path.join(base, spec["glb"])),
        mass=float(spec.get("mass") or 0.0),
        height=float(spec.get("height") or 0.0),
        plot=os.path.join(plots, f"fit_{name}.png") if plots else "",
        name_map=glb_to_rig.load_name_map(bone_map),
    )
    glb_to_rig.write_rig(rig, rig_path)
    # Muscle follows cross-section, so the square of height; age takes its share off that.
    rig["strength"] = round((rig["height_m"] / REFERENCE_HEIGHT) ** 2 * float(spec.get("age", 1.0)), 3)
    rig["speed"] = float(spec.get("speed", 1.0))
    rig["style"] = spec.get("style", {})
    rig["display"] = spec.get("display", name.upper())
    rig["persona"] = spec.get("persona", "")
    rig["says"] = spec.get("says", "")

    for attempt in range(10):
        with open(rig_path, "w", encoding="utf-8") as f:
            json.dump(rig, f, indent=2)
        z, cfg = rig_to_mjcf.build_one(rig, rig_path, name, models, cubes)
        if check(name, models) == 0:
            break
        for part in ("torso", "pelvis"):
            rig["radii"][part] = round(rig["radii"][part] * 0.94, 4)
        rig.setdefault("notes", []).append(
            f"trunk slimmed to torso {rig['radii']['torso']:.3f}, pelvis {rig['radii']['pelvis']:.3f}: its own arms touched it"
        )
        print(f"  {name}: slimming the trunk and building again (try {attempt + 2})", flush=True)
    else:
        raise SystemExit(f"{name}: its own parts still touch after ten tries; see the list above")
    print(
        f"  {name}: strength {rig['strength']:.2f}, speed {rig['speed']:.2f}, {cfg['total_mass_kg']:.1f} kg, "
        f"{rig['height_m']:.2f} m, radii {rig['radii']}"
    )
    return rig, z, cfg


def veteran(name: str, models: str, rigs: str) -> tuple:
    with open(os.path.join(rigs, f"{name}.json"), "r", encoding="utf-8") as f:
        rig = json.load(f)
    with open(os.path.join(models, f"{name}_policy_config.json"), "r", encoding="utf-8") as f:
        cfg = json.load(f)
    return rig, float(cfg["stand_height"]), cfg


def build(
    roster_path: str, only: list, models: str, rigs: str, plots: str, cubes: int = 0, every: bool = False
) -> dict:
    """The roster's boxers (only those named, if any; the rest are read back) and every pair model they are in.
    Returns {name: (rig, stand height, config)} of the boxers built or read."""
    with open(roster_path, "r", encoding="utf-8") as f:
        roster = json.load(f)
    base = os.path.dirname(os.path.abspath(roster_path))
    for folder in (models, rigs, plots):
        if folder:
            os.makedirs(folder, exist_ok=True)

    veterans = roster.get("veterans", {})
    specs = dict(roster["boxers"], **(veterans if every else {}))
    built = {}
    for name, spec in specs.items():
        if only and name not in only:
            # Already built on an earlier call: its files are read back, not made again.
            built[name] = veteran(name, models, rigs)
        else:
            built[name] = build_boxer(name, spec, models, rigs, plots, cubes, base)
    old = {name: veteran(name, models, rigs) for name in veterans if name not in built}

    print("\n==== match models", flush=True)
    new = list(specs)
    pairs = list(itertools.combinations(new, 2)) + [(n, v) for n in new for v in old]
    for a, b in pairs:
        if only and a not in only and b not in only:
            continue
        ra, za, ca = built[a]
        rb, zb, cb = built[b] if b in built else old[b]
        rig_to_mjcf.build_versus(ra, a, za, ca, rb, b, zb, cb, models, cubes)

    print("\n==== the roster")
    for name, (rig, z, cfg) in built.items():
        print(
            f"  {rig.get('display', name):9s} {rig['height_m']:.2f} m  {cfg['total_mass_kg']:5.1f} kg  "
            f"strength {cfg.get('strength', 1.0):.2f}  speed {cfg.get('speed', 1.0):.2f}  {rig.get('persona', '')}"
        )
    return built


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", action="append", default=[])
    ap.add_argument("--roster", default=os.path.join(HERE, "roster.json"))
    ap.add_argument("--models", default=os.path.join(HERE, "models", "v2"))
    ap.add_argument("--rigs", default=os.path.join(HERE, "rigs", "v2"))
    ap.add_argument("--plots", default=os.path.join(HERE, "logs"), help="where the pictures of each fit go")
    ap.add_argument("--cubes", type=int, default=0, help="cubes in the pool of every model (rig_to_mjcf.cubes_xml)")
    ap.add_argument("--all", action="store_true", help="derive the veterans from their meshes too")
    args = ap.parse_args()
    build(args.roster, args.only, args.models, args.rigs, args.plots, args.cubes, args.all)


if __name__ == "__main__":
    main()
