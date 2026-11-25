"""Training/tools/new_boxer.py and the retargeting it drives (glb_to_rig name maps and capsule fit, build_roster,
check_self_collision) on a tiny synthetic Mixamo-style humanoid written by the test itself: no mesh files, and
everything goes to tmp_path (nothing tracked is written)."""

from __future__ import annotations

import json
import os
import struct
import sys

import pytest

np = pytest.importorskip("numpy")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
for p in (os.path.join(ROOT, "Training"), os.path.join(ROOT, "Training", "tools")):
    if p not in sys.path:
        sys.path.insert(0, p)

import glb_to_rig  # noqa: E402

# A 1.86 m boxer in the rig frame (Blender Z-up, +x the character's left, -y forward): joint, parent, position.
_CENTRE = [
    ("Hips", None, (0.0, -0.014, 0.973)),
    ("Spine", "Hips", (0.0, -0.013, 1.069)),
    ("Spine1", "Spine", (0.0, -0.005, 1.21)),
    ("Spine2", "Spine1", (0.0, 0.004, 1.349)),
    ("Neck", "Spine2", (0.0, 0.052, 1.53)),
    ("Head", "Neck", (0.0, 0.025, 1.647)),
    ("HeadTop_End", "Head", (0.0, 0.025, 1.857)),
]
_LEFT = [
    ("Shoulder", "Spine2", (0.06, 0.037, 1.49)),
    ("Arm", "Shoulder", (0.202, 0.037, 1.51)),
    ("ForeArm", "Arm", (0.454, 0.037, 1.51)),
    ("Hand", "ForeArm", (0.711, 0.037, 1.51)),
    ("HandMiddle1", "Hand", (0.8, 0.037, 1.51)),
    ("HandMiddle2", "HandMiddle1", (0.825, 0.037, 1.51)),
    ("HandMiddle3", "HandMiddle2", (0.845, 0.037, 1.51)),
    ("HandMiddle4", "HandMiddle3", (0.865, 0.037, 1.51)),
    ("UpLeg", "Hips", (0.086, -0.015, 0.943)),
    ("Leg", "UpLeg", (0.086, -0.021, 0.528)),
    ("Foot", "Leg", (0.086, 0.011, 0.098)),
    ("ToeBase", "Foot", (0.086, -0.129, 0.022)),
    ("Toe_End", "ToeBase", (0.086, -0.171, 0.0)),
]


def skeleton():
    """[(name, parent, position)] in Mixamo's names without the prefix, the left side mirrored to the right."""
    out = list(_CENTRE)
    for side, sx in (("Left", 1.0), ("Right", -1.0)):
        for name, parent, (x, y, z) in _LEFT:
            par = parent if parent in {c[0] for c in _CENTRE} else side + parent
            out.append((side + name, par, (x * sx, y, z)))
    return [(n, p, np.array(pos)) for n, p, pos in out]


def _ring_cloud(a, b, r, n_len=14, n_around=14, depth=None):
    a, b = np.asarray(a, float), np.asarray(b, float)
    axis = (b - a) / np.linalg.norm(b - a)
    u = np.cross(axis, [0.0, 1.0, 0.0] if abs(axis[1]) < 0.9 else [1.0, 0.0, 0.0])
    u /= np.linalg.norm(u)
    w = np.cross(axis, u)
    t = np.linspace(0.0, 1.0, n_len)[:, None, None]
    ang = np.linspace(0.0, 2 * np.pi, n_around, endpoint=False)[None, :, None]
    ring = np.cos(ang) * u * r + np.sin(ang) * w * (depth or r)
    return (a + t * (b - a) + ring).reshape(-1, 3)


def _box(lo, hi, n=5):
    g = [np.linspace(lo[i], hi[i], n) for i in range(3)]
    return np.stack(np.meshgrid(*g, indexing="ij"), -1).reshape(-1, 3)


def write_glb(path: str, rename=lambda n: "mixamorig:" + n) -> None:
    """A skinned .glb: the joints as glTF nodes (Y-up, +z forward) named rename(Mixamo name), a vertex cloud around
    each part, each vertex weighted wholly to the bone it surrounds. No triangles: the reader needs only positions
    and weights."""
    bones = skeleton()
    idx = {name: i for i, (name, _, _) in enumerate(bones)}
    pos = {name: p for name, _, p in bones}

    clouds = []
    for side in ("Left", "Right"):
        for part, to, r in (("UpLeg", "Leg", 0.075), ("Leg", "Foot", 0.055), ("Arm", "ForeArm", 0.05)):
            clouds.append((side + part, _ring_cloud(pos[side + part], pos[side + to], r)))
        clouds.append((side + "ForeArm", _ring_cloud(pos[side + "ForeArm"], pos[side + "Hand"], 0.04)))
        clouds.append((side + "Hand", _ring_cloud(pos[side + "Hand"], pos[side + "HandMiddle4"], 0.035)))
        x = pos[side + "Foot"][0]
        clouds.append((side + "Foot", _box((x - 0.045, -0.12, 0.0), (x + 0.045, 0.06, 0.09))))
        clouds.append((side + "ToeBase", _box((x - 0.04, -0.2, 0.0), (x + 0.04, -0.12, 0.04))))
    clouds.append(("Hips", _ring_cloud((0, -0.01, 0.85), (0, -0.01, 1.07), 0.16, depth=0.11)))
    clouds.append(("Spine", _ring_cloud((0, 0.0, 1.07), (0, 0.0, 1.3), 0.17, depth=0.11)))
    clouds.append(("Spine2", _ring_cloud((0, 0.0, 1.3), (0, 0.0, 1.52), 0.17, depth=0.11)))
    clouds.append(("Neck", _ring_cloud(pos["Neck"], pos["Head"], 0.05)))
    v = np.random.default_rng(0).normal(size=(600, 3))
    clouds.append(("Head", (0.0, 0.02, 1.75) + 0.105 * v / np.linalg.norm(v, axis=1, keepdims=True)))

    verts = np.concatenate([c for _, c in clouds]).astype(np.float32)
    joints = np.zeros((len(verts), 4), np.uint16)
    joints[:, 0] = np.concatenate([[idx[b]] * len(c) for b, c in clouds])
    weights = np.zeros((len(verts), 4), np.float32)
    weights[:, 0] = 1.0
    gl = lambda p: [float(p[0]), float(p[2]), float(-p[1])]  # rig frame (x, y, z) -> glTF (x, z, -y)  # noqa: E731
    verts = np.array([gl(p) for p in verts], np.float32)

    nodes = []
    for name, parent, p in bones:
        local = p - (pos[parent] if parent else 0.0)
        kids = [idx[n] for n, par, _ in bones if par == name]
        node = {"name": rename(name), "translation": gl(local)}
        if kids:
            node["children"] = kids
        nodes.append(node)
    nodes.append({"name": "Body", "mesh": 0, "skin": 0})
    blob = verts.tobytes() + joints.tobytes() + weights.tobytes()
    n = len(verts)
    gltf = {
        "asset": {"version": "2.0"},
        "scene": 0,
        "scenes": [{"nodes": [0, len(nodes) - 1]}],
        "nodes": nodes,
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "JOINTS_0": 1, "WEIGHTS_0": 2}}]}],
        "skins": [{"joints": list(range(len(bones)))}],
        "buffers": [{"byteLength": len(blob)}],
        "bufferViews": [
            {"buffer": 0, "byteOffset": 0, "byteLength": n * 12},
            {"buffer": 0, "byteOffset": n * 12, "byteLength": n * 8},
            {"buffer": 0, "byteOffset": n * 20, "byteLength": n * 16},
        ],
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": n, "type": "VEC3"},
            {"bufferView": 1, "componentType": 5123, "count": n, "type": "VEC4"},
            {"bufferView": 2, "componentType": 5126, "count": n, "type": "VEC4"},
        ],
    }
    js = json.dumps(gltf).encode()
    js += b" " * (-len(js) % 4)
    blob += b"\0" * (-len(blob) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(js) + 8 + len(blob)))
        f.write(struct.pack("<I4s", len(js), b"JSON") + js)
        f.write(struct.pack("<I4s", len(blob), b"BIN\0") + blob)


@pytest.fixture
def mixamo_glb(tmp_path):
    path = str(tmp_path / "Rocky.glb")
    write_glb(path)
    return path


def test_mixamo_preset_names_every_bone_and_capsules_fit_inside_the_clouds(mixamo_glb):
    rig = glb_to_rig.rig_from_glb(mixamo_glb, name_map=glb_to_rig.load_name_map("mixamo"))
    assert set(glb_to_rig.REQUIRED) <= set(rig["map"])
    assert rig["map"]["thigh_l"] == "mixamorig:LeftUpLeg" and rig["map"]["torso"] == "mixamorig:Spine2"
    assert rig["map"]["hand_tip_r"] == "mixamorig:RightHandMiddle3"
    assert 1.8 < rig["height_m"] < 1.9
    radii = rig["radii"]
    # Fitted from each bone's own vertices: a little under the cloud's radius (the thin way for the trunk).
    for part, r in (("thigh", 0.075), ("shin", 0.055), ("upper_arm", 0.05), ("forearm", 0.04)):
        assert 0.85 * r < radii[part] < r, (part, radii[part])
    assert 0.08 < radii["torso"] < 0.11 and 0.08 < radii["pelvis"] < 0.11
    for part, (lo, hi) in glb_to_rig.RADIUS_BAND.items():
        assert lo <= radii[part] <= hi


def test_a_name_map_file_names_bones_the_guess_cannot(tmp_path):
    path = str(tmp_path / "odd.glb")
    write_glb(path, rename=lambda n: "bone_" + n.replace("Left", "Lf").replace("Right", "Rt"))
    names = {k: v.replace("Left", "Lf").replace("Right", "Rt") for k, v in glb_to_rig.MIXAMO.items()}
    map_path = tmp_path / "map.json"
    map_path.write_text(json.dumps({k: "bone_" + v for k, v in names.items()}))
    rig = glb_to_rig.rig_from_glb(path, name_map=glb_to_rig.load_name_map(str(map_path)))
    assert rig["map"]["thigh_l"] == "bone_LfUpLeg" and rig["map"]["forearm_r"] == "bone_RtForeArm"
    assert rig["map"]["pelvis"] == "bone_Hips"
    bad = tmp_path / "bad.json"
    bad.write_text(json.dumps({"elbow": "x"}))
    with pytest.raises(SystemExit):
        glb_to_rig.load_name_map(str(bad))


def test_roster_entry_is_inserted_without_touching_the_rest(tmp_path):
    import new_boxer

    text = '{\n  "about": "x",\n  "boxers": {\n    "kim": {"display": "KIM", "style": {"a": 1}}\n  }\n}\n'
    path = tmp_path / "roster.json"
    path.write_text(text)
    entry = {"display": "ROCKY", "glb": "SK_Rocky.glb", "persona": "boxer", "style": {}}
    new_boxer.add_to_roster(str(path), "rocky", entry)
    out = path.read_text()
    assert out.startswith('{\n  "about": "x",\n  "boxers": {\n    "kim": {"display": "KIM", "style": {"a": 1}},\n')
    assert json.loads(out)["boxers"] == {"kim": {"display": "KIM", "style": {"a": 1}}, "rocky": entry}
    with pytest.raises(SystemExit):
        new_boxer.add_to_roster(str(path), "kim", entry)


@pytest.mark.physics
def test_new_boxer_builds_checks_and_adds_the_roster_entry(tmp_path, mixamo_glb):
    pytest.importorskip("mujoco")
    import new_boxer
    from check_self_collision import check

    roster = tmp_path / "roster.json"
    roster.write_text('{\n  "boxers": {\n  }\n}\n')
    models, rigs = tmp_path / "models", tmp_path / "rigs"
    args = [
        "rocky",
        mixamo_glb,
        "--bone-map",
        "mixamo",
        "--persona",
        "swarmer",
        "--no-plot",
        "--roster",
        str(roster),
        "--models",
        str(models),
        "--rigs",
        str(rigs),
        "--mesh-dir",
        str(tmp_path / "meshes"),
    ]
    assert new_boxer.main(args) == 0
    for f in ("rocky_solo.xml", "rocky_bag.xml", "rocky_spar.xml", "rocky_policy_config.json"):
        assert (models / f).exists(), f
    assert check("rocky", str(models)) == 0
    entry = json.loads(roster.read_text())["boxers"]["rocky"]
    assert entry["bone_map"] == "mixamo" and entry["persona"] == "swarmer"
    assert entry["glb"] == "meshes/SK_Rocky.glb" and (tmp_path / "meshes" / "SK_Rocky.glb").exists()
    cfg = json.loads((models / "rocky_policy_config.json").read_text())
    assert len(cfg["joint_order"]) == 21 and 60.0 < cfg["total_mass_kg"] < 100.0
    # Built once: a second call refuses, and leaves the entry alone.
    with pytest.raises(SystemExit):
        new_boxer.main(args)
