"""A new boxer from a humanoid .glb, in one command: rig, collision shapes, MuJoCo models, the checks, the roster.

    python tools/new_boxer.py rocky path/to/Rocky.glb
    python tools/new_boxer.py rocky path/to/Rocky.glb --bone-map mixamo --height 1.80 --persona swarmer --pairs

The steps are the roster's own (tools/build_roster.build_boxer), nothing is done twice:
  1. the mesh: a .glb outside the repo is copied to Assets/_Project/Environments/Models/SK_<Name>.glb (the roster
     must not hold an absolute local path; Unity writes the .meta on its next import);
  2. glb_to_rig.rig_from_glb reads the skeleton and fits a capsule inside the skin of each part, from the
     vertices each bone moves most (fit_radii). --bone-map names the bones when the guess cannot: a preset
     ("mixamo": Hips, Spine, LeftUpLeg, ..., with or without the mixamorig: prefix) or a JSON file of
     {canonical key: bone name} (keys in glb_to_rig.CANONICAL);
  3. rig_to_mjcf.build_one writes <name>_solo/_bag/_spar.xml and <name>_policy_config.json;
  4. tools/check_self_collision.check puts the body through the T-pose, the guard and a swing; while a part touches
     another the trunk is slimmed and the body built again (house rule: verified before training);
  5. only then the entry goes into roster.json (inserted as text: the hand-kept layout of the rest is left alone).
--pairs also builds the match models against every boxer already built.

Exits 1 (and writes no roster entry) if the mesh has no skeleton, a required bone cannot be found, or the body
still touches itself after ten tries. What is left to do by hand (v3 body, Unity prefab, training) is printed.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # Training/
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "tools"))
import build_roster  # noqa: E402
import glb_to_rig  # noqa: E402

MESH_DIR = os.path.join(REPO, "Assets", "_Project", "Environments", "Models")
NAME = re.compile(r"^[a-z][a-z0-9_]{1,23}$")


def inside(path: str, folder: str) -> bool:
    try:
        return not os.path.relpath(os.path.abspath(path), os.path.abspath(folder)).startswith("..")
    except ValueError:  # another drive
        return False


def place_mesh(glb: str, display: str, mesh_dir: str) -> str:
    """The mesh where the roster can name it relative to the repo: left where it is if it is in the repo already,
    otherwise copied into mesh_dir as SK_<Display>.glb (the skinned-mesh prefix of the naming rule)."""
    if inside(glb, REPO) and inside(mesh_dir, REPO):
        return os.path.abspath(glb)
    os.makedirs(mesh_dir, exist_ok=True)
    dest = os.path.join(mesh_dir, f"SK_{display.title().replace(' ', '')}.glb")
    if os.path.abspath(dest) != os.path.abspath(glb):
        shutil.copyfile(glb, dest)
        print(f"copied the mesh to {dest}")
    return dest


def relative_to(path: str, folder: str) -> str:
    try:
        rel = os.path.relpath(os.path.abspath(path), os.path.abspath(folder))
    except ValueError:
        raise SystemExit(f"{path} is on another drive from {folder}: the roster cannot name it by a relative path")
    return rel.replace("\\", "/")


def roster_entry(args: argparse.Namespace, glb_rel: str, bone_map: str) -> dict:
    entry = {"display": args.display or args.name.upper(), "glb": glb_rel}
    if args.height:
        entry["height"] = args.height
    if args.mass:
        entry["mass"] = args.mass
    if bone_map:
        entry["bone_map"] = bone_map
    entry.update(persona=args.persona, says=args.says, age=args.age, speed=args.speed, style={})
    return entry


def _closing_brace(text: str, open_at: int) -> int:
    """Index of the brace that closes the one at open_at, skipping strings."""
    depth, i, in_str = 0, open_at, False
    while i < len(text):
        c = text[i]
        if in_str:
            if c == "\\":
                i += 1
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    raise ValueError("unbalanced braces")


def add_to_roster(roster_path: str, name: str, entry: dict) -> None:
    """Puts the entry at the end of "boxers", as text, in the layout the file is kept in (one field a line, the
    style on one line), so the rest of the file is byte for byte as it was. Checked by reading it back."""
    with open(roster_path, "r", encoding="utf-8") as f:
        text = f.read()
    roster = json.loads(text)
    if name in roster.get("boxers", {}) or name in roster.get("veterans", {}):
        raise SystemExit(f"{name} is already in {roster_path}")
    key = re.search(r'"boxers"\s*:\s*\{', text)
    if key is None:
        raise SystemExit(f'{roster_path} has no "boxers"')
    close = _closing_brace(text, key.end() - 1)
    fields = ",\n".join(f"      {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}" for k, v in entry.items())
    block = f"    {json.dumps(name)}: {{\n{fields}\n    }}"
    body = text[key.end() : close].rstrip()
    head = text[: key.end()] + body + ("," if body.strip() else "") + "\n" + block + "\n  "
    new_text = head + text[close:]
    if json.loads(new_text)["boxers"].get(name) != entry:
        raise SystemExit("the roster entry did not read back as written; roster left alone")
    with open(roster_path, "w", encoding="utf-8") as f:
        f.write(new_text)


def build_pairs(name: str, built: tuple, roster_path: str, models: str, rigs: str, cubes: int) -> list:
    """The match models of the new boxer against every boxer whose files are already in models/rigs."""
    import rig_to_mjcf

    with open(roster_path, "r", encoding="utf-8") as f:
        roster = json.load(f)
    rig, z, cfg = built
    done = []
    for other in list(roster.get("boxers", {})) + list(roster.get("veterans", {})):
        if other == name:
            continue
        if not os.path.exists(os.path.join(models, f"{other}_policy_config.json")) or not os.path.exists(
            os.path.join(rigs, f"{other}.json")
        ):
            continue
        ro, zo, co = build_roster.veteran(other, models, rigs)
        rig_to_mjcf.build_versus(rig, name, z, cfg, ro, other, zo, co, models, cubes)
        done.append(other)
    return done


def can_plot() -> bool:
    try:
        import PIL  # noqa: F401
    except ImportError:
        return False
    return True


def main(argv: list | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("name", help="the boxer's file name: lower case, letters, digits and _")
    ap.add_argument("glb", help="the humanoid's skinned mesh, .glb")
    ap.add_argument("--display", default="", help="the name on screen (default: NAME in capitals)")
    ap.add_argument("--height", type=float, default=0.0, help="metres; 0 reads it off the mesh")
    ap.add_argument("--mass", type=float, default=0.0, help="kg; 0 from the mesh's volume and height")
    ap.add_argument("--bone-map", default="", help="a glb_to_rig name map: a preset (mixamo) or a JSON file")
    ap.add_argument("--persona", default="boxer")
    ap.add_argument("--says", default="")
    ap.add_argument("--age", type=float, default=1.0, help="muscle strength beside a trained adult's")
    ap.add_argument("--speed", type=float, default=1.0, help="joint speed beside a trained adult's")
    ap.add_argument("--pairs", action="store_true", help="also build the match models against the boxers built")
    ap.add_argument("--cubes", type=int, default=0)
    ap.add_argument("--no-plot", action="store_true", help="no picture of the fit (it needs Pillow)")
    ap.add_argument("--roster", default=os.path.join(HERE, "roster.json"))
    ap.add_argument("--models", default=os.path.join(HERE, "models", "v2"))
    ap.add_argument("--rigs", default=os.path.join(HERE, "rigs", "v2"))
    ap.add_argument("--plots", default=os.path.join(HERE, "logs"))
    ap.add_argument("--mesh-dir", default=MESH_DIR, help="where a mesh from outside the repo is copied to")
    args = ap.parse_args(argv)

    if not NAME.match(args.name):
        ap.error("NAME must be 2-24 characters: a lower-case letter, then letters, digits or _")
    if not os.path.isfile(args.glb) or not args.glb.lower().endswith(".glb"):
        ap.error(f"{args.glb} is not a .glb file")
    roster_dir = os.path.dirname(os.path.abspath(args.roster))
    with open(args.roster, "r", encoding="utf-8") as f:
        roster = json.load(f)
    if args.name in roster.get("boxers", {}) or args.name in roster.get("veterans", {}):
        ap.error(f"{args.name} is already in the roster; build it with tools/build_roster.py --only {args.name}")
    bone_map = args.bone_map
    if bone_map and bone_map.lower() not in glb_to_rig.NAME_MAPS:
        glb_to_rig.load_name_map(bone_map)  # fails now, with the keys it wants, rather than after the copy
        bone_map = relative_to(bone_map, roster_dir)

    display = args.display or args.name.upper()
    glb = place_mesh(args.glb, display, args.mesh_dir)
    entry = roster_entry(args, relative_to(glb, roster_dir), bone_map)
    for folder in (args.models, args.rigs):
        os.makedirs(folder, exist_ok=True)
    plots = "" if args.no_plot or not can_plot() else args.plots
    if plots:
        os.makedirs(plots, exist_ok=True)

    try:
        built = build_roster.build_boxer(args.name, entry, args.models, args.rigs, plots, args.cubes, roster_dir)
    except SystemExit as e:
        print(f"{args.name}: not added to the roster: {e}")
        return 1
    add_to_roster(args.roster, args.name, entry)
    print(f"added {args.name} to {args.roster}")
    if args.pairs:
        done = build_pairs(args.name, built, args.roster, args.models, args.rigs, args.cubes)
        print(f"match models against: {', '.join(done) or 'nobody (no other boxer is built here)'}")

    head = getattr(glb_to_rig.fit_radii, "thickness", {}).get("head")
    print("\nleft to do by hand:")
    if head:
        print(f'  * models/v3: add "{args.name}" to tools/build_v3.py NAMES and HEAD_HALF["{args.name}"] = {head}')
    print(f'  * style: fill in its "style" in roster.json (keys read in envs/boxing.py), then --only {args.name}')
    print(
        f'  * Unity: MjRetrofit.BuildBoxer("{args.name}"), then menu PoBox/Mj/Dress Every Boxer For Both Corners; '
        "add the Red/Blue prefabs to Arena.unity"
    )
    print(f"  * train: train_box.py --stage footwork --xml models/v2/{args.name}_solo.xml, warm from a shipped brain")
    return 0


if __name__ == "__main__":
    sys.exit(main())
