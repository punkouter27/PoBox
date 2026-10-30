"""Each boxer's segment masses against de Leva (1996) (2026-10-30, item 42). Reads the bodies, changes nothing.

    python tools/mass_audit.py                 # every boxer in models/v2, a table on stdout and tools/mass_audit.md
    python tools/mass_audit.py --models models/v3

de Leva P. (1996), "Adjustments to Zatsiorsky-Seluyanov's segment inertia parameters", J. Biomech. 29(9):1223-1230,
table 4: segment mass as a % of body mass, male / female. The model's bodies are coarser than de Leva's segments, so
they are compared as groups:
    pelvis body   = lower trunk                          (pelvis_geom + pelvis_up)
    torso body    = middle trunk + upper trunk + head    (torso_geom + chest_geom + head_geom)
    forearm body  = forearm + hand, less the 10 oz glove (forearm_geom + glove geom; rig_to_mjcf.py GLOVE_KG)
    upper arm, thigh, shin (shank), foot                 one to one
The share is of the body without its gloves (the rig's own mass, as de Leva's subjects had none). rig_to_mjcf.py takes
its fractions from Winter / Dempster (cadaver data); de Leva's come from living subjects (gamma-ray scanning) and put
about 4 points more in the thigh and about as much less in the trunk and head.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import sys

import mujoco

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)

# de Leva (1996) table 4, % of body mass: (male, female).
DE_LEVA = {
    "head": (6.94, 6.68), "upper_trunk": (15.96, 15.45), "middle_trunk": (16.33, 14.65), "lower_trunk": (11.17, 12.47),
    "upper_arm": (2.71, 2.55), "forearm": (1.62, 1.38), "hand": (0.61, 0.56),
    "thigh": (14.16, 14.78), "shank": (4.33, 4.81), "foot": (1.37, 1.29),
}
# The model's bodies as groups of de Leva's segments.
GROUPS = {
    "pelvis": ("lower_trunk",),
    "torso": ("middle_trunk", "upper_trunk", "head"),
    "upper_arm": ("upper_arm",),
    "forearm": ("forearm", "hand"),
    "thigh": ("thigh",),
    "shin": ("shank",),
    "foot": ("foot",),
}
FEMALE = {"grandma"}          # the roster's women; the rest are compared with de Leva's men
GLOVE_KG = 0.283              # rig_to_mjcf.py: a 10 oz glove, on the hand
FLAG = 0.20                   # a group more than this share off de Leva's is flagged


def audit(xml: str, name: str, female: bool) -> list:
    m = mujoco.MjModel.from_xml_path(xml)
    body = lambda n: m.body_mass[mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_BODY, "a_" + n)]
    total = float(m.body_subtreemass[mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_BODY, "a_pelvis")])
    bare = total - 2 * GLOVE_KG
    rows = []
    for group, segs in GROUPS.items():
        sides = ("_l", "_r") if group not in ("pelvis", "torso") else ("",)
        kg = sum(float(body(group + s)) for s in sides) / len(sides)
        if group == "forearm":
            kg -= GLOVE_KG
        model = 100.0 * kg / bare
        ref = sum(DE_LEVA[s][1 if female else 0] for s in segs)
        rows.append({"boxer": name, "sex": "F" if female else "M", "segment": group, "kg": kg, "model_pct": model,
                     "de_leva_pct": ref, "ratio": model / ref, "flag": abs(model / ref - 1.0) > FLAG})
    return rows, bare


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--models", default=os.path.join("models", "v2"))
    ap.add_argument("--out", default=os.path.join("tools", "mass_audit.md"))
    args = ap.parse_args()
    lines = ["# Segment masses against de Leva (1996)", "",
             "Share of the boxer's mass without gloves; ratio = model / de Leva; ** = more than 20% off.", "",
             "| boxer | sex | kg (no gloves) | " + " | ".join(GROUPS) + " |", "|" + "---|" * (3 + len(GROUPS))]
    worst = []
    for solo in sorted(glob.glob(os.path.join(HERE, args.models, "*_solo.xml"))):
        name = os.path.basename(solo)[:-len("_solo.xml")]
        rows, bare = audit(solo, name, name in FEMALE)
        cells = [f"{'**' if r['flag'] else ''}{r['model_pct']:.1f} / {r['de_leva_pct']:.1f} ({r['ratio']:.2f}){'**' if r['flag'] else ''}" for r in rows]
        lines.append(f"| {name} | {rows[0]['sex']} | {bare:.1f} | " + " | ".join(cells) + " |")
        worst += [r for r in rows if r["flag"]]
    lines += ["", "Each cell: model % / de Leva % (ratio).", ""]
    if worst:
        lines.append("Flagged (more than 20% off):")
        for r in worst:
            lines.append(f"- {r['boxer']} {r['segment']}: {r['model_pct']:.1f}% against {r['de_leva_pct']:.1f}% "
                         f"({r['kg']:.2f} kg, x{r['ratio']:.2f})")
    text = "\n".join(lines) + "\n"
    print(text)
    out = os.path.join(HERE, args.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as fh:
        fh.write(text)
    print(f"written to {out}")


if __name__ == "__main__":
    main()
