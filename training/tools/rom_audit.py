"""Each boxer's joint ranges against a person's range of motion (2026-10-30, item 43). Reads the bodies, changes nothing.

    python tools/rom_audit.py                 # every boxer in models/v2; a table on stdout and tools/rom_audit.md

Which way of a joint is which movement is found from the body, not assumed: each joint is turned a little each way from
the anatomical zero (everything at 0, the arm hanging at the side for the shoulder's swing) and the movement of a point
past it says what it is (the knee going forward is hip flexion, the toes coming up dorsiflexion, and so on). The
limits are then read as degrees of that movement from the anatomical zero, so the left and right sides, mirrored in the
model, read alike.

Reference: active range of motion of healthy adults, American Academy of Orthopaedic Surgeons (Greene & Heckman 1994,
"The Clinical Measurement of Joint Motion") and Norkin & White, "Measurement of Joint Motion" (5th ed., 2016), rounded:
    trunk (thoracolumbar)  flexion 80, extension 30, lateral bend 35, rotation 45
    shoulder               flexion 180, extension 60, abduction 180, adduction (across, from the side) 45
    elbow                  flexion 150, extension 0 (a few degrees of hyperextension are normal)
    hip                    flexion 120, extension 30, abduction 45, adduction 30, internal 45, external 45
    knee                   flexion 135, extension 0
    ankle                  dorsiflexion 20, plantarflexion 50; inversion 35, eversion 15 (subtalar)
A limit more than 15 degrees past the person's is flagged "past"; one under half of it "short" (a boxer may not need
it; the flag only says the body cannot).
"""
from __future__ import annotations

import argparse
import glob
import json
import math
import os

import mujoco
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HUMAN = {   # joint kind: ((movement, degrees), (movement, degrees))
    "abdomen_y": (("flexion", 80), ("extension", 30)),
    "abdomen_x": (("lateral bend", 35), ("lateral bend", 35)),
    "abdomen_z": (("rotation", 45), ("rotation", 45)),
    "shoulder_x": (("abduction", 180), ("adduction", 45)),
    "shoulder_z": (("flexion", 180), ("extension", 60)),
    "elbow": (("flexion", 150), ("extension", 0)),
    "hip_y": (("flexion", 120), ("extension", 30)),
    "hip_x": (("abduction", 45), ("adduction", 30)),
    "hip_z": (("internal", 45), ("external", 45)),
    "knee": (("flexion", 135), ("extension", 0)),
    "ankle_y": (("dorsiflexion", 20), ("plantarflexion", 50)),
    "ankle_x": (("inversion", 35), ("eversion", 15)),
}
PAST, SHORT = 15.0, 0.5


class Body:
    def __init__(self, xml: str):
        self.m = mujoco.MjModel.from_xml_path(xml)
        self.d = mujoco.MjData(self.m)
        self.J = lambda n: int(self.m.jnt_qposadr[mujoco.mj_name2id(self.m, mujoco.mjtObj.mjOBJ_JOINT, "a_" + n)])
        self.B = lambda n: mujoco.mj_name2id(self.m, mujoco.mjtObj.mjOBJ_BODY, "a_" + n)
        self.G = lambda n: mujoco.mj_name2id(self.m, mujoco.mjtObj.mjOBJ_GEOM, "a_" + n)

    def pose(self, q: dict):
        m, d = self.m, self.d
        d.qpos[:] = m.qpos0
        d.qpos[0:3] = [0.0, 0.0, 1.5]
        d.qpos[3:7] = [1.0, 0.0, 0.0, 0.0]               # facing +x, y left, z up
        for k, v in q.items():
            d.qpos[self.J(k)] = v
        mujoco.mj_kinematics(m, d)
        return d

    def arm_down(self, side: str) -> float:
        """shoulder_x with the arm hanging at the side (the elbow straight under the shoulder)."""
        best, at = 9.0, 0.0
        for q in np.radians(np.arange(-180.0, 180.5, 0.5)):
            d = self.pose({f"shoulder_x_{side}": q})
            v = d.xpos[self.B(f"forearm_{side}")] - d.xpos[self.B(f"upper_arm_{side}")]
            err = math.acos(max(-1.0, min(1.0, -v[2] / np.linalg.norm(v))))
            if err < best:
                best, at = err, q
        return at


def movement(body: Body, joint: str, base: dict, side: str) -> int:
    """+1 if turning the joint positive is the first movement of HUMAN[kind], -1 if it is the second."""
    kind = joint[:-2] if joint.endswith(("_l", "_r")) else joint
    lat = 1.0 if side == "l" else -1.0                     # away from the midline is +y on the left
    probe = math.radians(10.0)

    def where(q):
        d = body.pose({**base, joint: base.get(joint, 0.0) + q})
        s = side
        if kind.startswith("abdomen"):
            head = d.geom_xpos[body.G("head_geom")]
            return {"x": head[0], "y": head[1], "fwd": d.xmat[body.B("torso")].reshape(3, 3)[:, 0]}
        if kind == "shoulder_x" or kind == "shoulder_z":
            return {"elbow": d.xpos[body.B(f"forearm_{s}")] - d.xpos[body.B(f"upper_arm_{s}")]}
        if kind == "elbow":
            return {"reach": np.linalg.norm(d.geom_xpos[body.G(f"glove_{s}")] - d.xpos[body.B(f"upper_arm_{s}")])}
        if kind in ("hip_y", "hip_x", "knee"):
            return {"knee": d.xpos[body.B(f"shin_{s}")], "ankle": d.xpos[body.B(f"foot_{s}")]}
        R = d.geom_xmat[body.G(f"foot_{s}_geom")].reshape(3, 3)
        return {"toe": R[:, 0], "up": R[:, 2]}

    copy = lambda w: {k: np.array(v, dtype=float) for k, v in w.items()}     # MjData fields are views: keep this pose's numbers
    a, b = copy(where(-probe)), copy(where(probe))
    if kind == "abdomen_y":
        first = b["x"] > a["x"]                            # the head forward: flexion
    elif kind in ("abdomen_x", "abdomen_z"):
        return 1                                           # symmetric movements
    elif kind == "shoulder_x":
        first = lat * b["elbow"][1] > lat * a["elbow"][1]  # the elbow out from the side: abduction
    elif kind == "shoulder_z":
        first = b["elbow"][0] > a["elbow"][0]              # the elbow forward: flexion
    elif kind == "elbow":
        first = b["reach"] < a["reach"]                    # the glove nearer the shoulder: flexion (probed mid-range)
    elif kind == "hip_y":
        first = b["knee"][0] > a["knee"][0]                # the knee forward: flexion
    elif kind == "hip_x":
        first = lat * b["knee"][1] > lat * a["knee"][1]    # the knee away from the midline: abduction
    elif kind == "knee":
        first = b["ankle"][0] < a["ankle"][0]              # the ankle back: flexion
    elif kind == "hip_z":
        first = lat * b["toe"][1] < lat * a["toe"][1]      # the toes towards the midline: internal rotation
    elif kind == "ankle_y":
        first = b["toe"][2] > a["toe"][2]                  # the toes up: dorsiflexion
    elif kind == "ankle_x":
        first = lat * b["up"][1] < lat * a["up"][1]        # the sole turned to the midline: inversion
    else:
        raise ValueError(joint)
    return 1 if first else -1


def audit(xml: str, cfg: dict) -> list:
    body = Body(xml)
    zero = {s: body.arm_down(s) for s in "lr"}
    rows = []
    for j, lo, hi in zip(cfg["joint_order"], cfg["lower"], cfg["upper"]):
        side = j[-1] if j.endswith(("_l", "_r")) else "l"
        kind = j[:-2] if j.endswith(("_l", "_r")) else j
        base = {f"shoulder_x_{s}": zero[s] for s in "lr"} if kind == "shoulder_z" else {}
        q0 = zero[side] if kind == "shoulder_x" else 0.0
        # Probed where both ways move the point apart: at the arm's zero for the shoulder, mid-range for the elbow (straight,
        # either way only shortens the reach).
        at = {j: q0} if kind == "shoulder_x" else {j: 0.5 * (lo + hi)} if kind == "elbow" else {}
        sign = movement(body, j, {**base, **at}, side)
        # Degrees of each movement from the anatomical zero: the first is on the side of `sign`.
        first = math.degrees((hi - q0) if sign > 0 else (q0 - lo))
        second = math.degrees((q0 - lo) if sign > 0 else (hi - q0))
        (m1, h1), (m2, h2) = HUMAN[kind]
        flag = lambda deg, ref: ("past" if deg > ref + PAST else "short" if ref > 0 and deg < SHORT * ref else "")
        rows.append({"joint": j, "first": (m1, first, h1, flag(first, h1)), "second": (m2, second, h2, flag(second, h2)),
                     "zero_deg": math.degrees(q0)})
    return rows


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--models", default=os.path.join("models", "v2"))
    ap.add_argument("--out", default=os.path.join("tools", "rom_audit.md"))
    args = ap.parse_args()
    out = ["# Joint ranges against a person's range of motion", "",
           "Degrees from the anatomical zero: model / person; past = more than 15 past the person's, short = under half of it.", ""]
    tables, flagged = {}, []
    for solo in sorted(glob.glob(os.path.join(HERE, args.models, "*_solo.xml"))):
        name = os.path.basename(solo)[:-len("_solo.xml")]
        with open(os.path.join(HERE, args.models, f"{name}_policy_config.json"), "r", encoding="utf-8") as fh:
            cfg = json.load(fh)
        rows = audit(solo, cfg)
        key = json.dumps([(r["joint"], round(r["first"][1]), round(r["second"][1])) for r in rows])
        tables.setdefault(key, (rows, []))[1].append(name)
    for rows, names in tables.values():
        out += [f"## {', '.join(names)}", "", "| joint | movement | model | person | | movement | model | person | |", "|---|---|---|---|---|---|---|---|---|"]
        for r in rows:
            (m1, a1, h1, f1), (m2, a2, h2, f2) = r["first"], r["second"]
            out.append(f"| {r['joint']} | {m1} | {a1:.0f} | {h1} | {f1} | {m2} | {a2:.0f} | {h2} | {f2} |")
            for mv, deg, ref, fl in (r["first"], r["second"]):
                if fl:
                    flagged.append(f"{'/'.join(names)} {r['joint']} {mv}: {deg:.0f} against {ref} ({fl})")
        out.append("")
    out.append("Flagged:" if flagged else "Nothing flagged.")
    out += [f"- {f}" for f in flagged]
    text = "\n".join(out) + "\n"
    print(text)
    path = os.path.join(HERE, args.out)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)
    print(f"written to {path}")


if __name__ == "__main__":
    main()
