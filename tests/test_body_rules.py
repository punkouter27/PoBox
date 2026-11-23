"""The physics tier: every boxer's game body (Training/models/v3, the MJCF Unity's prefabs compile to) keeps the
Po standard's body rules, checked on the compiled MuJoCo model:

- Earth gravity, and a body in the air falls as it should (z = z0 - g t^2 / 2);
- every hinge has a real range (limited, lower < upper);
- every hinge is driven by a PD position actuator (kp > 0, the bias is -kp q - kv qdot, a force range);
- no two parts of one boxer touch in its default stance (parent-child pairs and overlapping pairs are excluded);
- the floor and the feet have friction.
"""

import glob
import json
import os

import pytest

np = pytest.importorskip("numpy")
mujoco = pytest.importorskip("mujoco")

pytestmark = pytest.mark.physics

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = sorted(glob.glob(os.path.join(ROOT, "Training", "models", "v3", "*_solo.xml")))
BOXERS = os.path.join(ROOT, "Assets", "_Project", "Agents", "Boxers")


def _name(xml):
    return os.path.basename(xml)[: -len("_solo.xml")]


def _load(xml):
    m = mujoco.MjModel.from_xml_path(xml)
    return m, mujoco.MjData(m)


def _hinges(m):
    return [j for j in range(m.njnt) if m.jnt_type[j] == mujoco.mjtJoint.mjJNT_HINGE]


def _config(name):
    for d in os.listdir(BOXERS):
        if d.lower() == name:
            with open(os.path.join(BOXERS, d, "config.json"), encoding="utf-8") as f:
                return json.load(f)
    pytest.skip(f"no config.json for {name}")


def test_there_are_bodies():
    assert len(MODELS) >= 8, MODELS


@pytest.mark.parametrize("xml", MODELS, ids=_name)
def test_gravity_pulls_a_body_in_the_air_down(xml):
    m, d = _load(xml)
    np.testing.assert_allclose(m.opt.gravity, [0.0, 0.0, -9.81], atol=1e-9)
    d.qpos[2] += 2.0  # the boxer's free root, well above the floor: nothing touches it for the first 0.2 s
    mujoco.mj_forward(m, d)
    z0, t = float(d.qpos[2]), 0.0
    while t < 0.2 - 1e-9:
        mujoco.mj_step(m, d)
        t += m.opt.timestep
    # implicitfast with joint damping: within 2 cm of free fall (1.96 m/s^2 * 0.04 s^2 = 0.196 m)
    assert abs((z0 - float(d.qpos[2])) - 0.5 * 9.81 * t * t) < 0.02


@pytest.mark.parametrize("xml", MODELS, ids=_name)
def test_every_hinge_has_a_range(xml):
    m, _ = _load(xml)
    hinges = _hinges(m)
    assert len(hinges) >= 21
    for j in hinges:
        lo, hi = m.jnt_range[j]
        assert m.jnt_limited[j], mujoco.mj_id2name(m, mujoco.mjtObj.mjOBJ_JOINT, j)
        assert lo < hi and hi - lo <= 2 * np.pi, mujoco.mj_id2name(m, mujoco.mjtObj.mjOBJ_JOINT, j)


@pytest.mark.parametrize("xml", MODELS, ids=_name)
def test_every_hinge_has_a_pd_drive(xml):
    m, _ = _load(xml)
    driven = {int(m.actuator_trnid[a, 0]): a for a in range(m.nu)}
    for j in _hinges(m):
        name = mujoco.mj_id2name(m, mujoco.mjtObj.mjOBJ_JOINT, j)
        if not name.startswith("a_"):
            continue
        assert j in driven, f"{name} has no actuator"
        a = driven[j]
        kp = m.actuator_gainprm[a, 0]
        assert kp > 0.0, name
        assert m.actuator_biasprm[a, 1] == pytest.approx(-kp), name  # -kp * q
        assert m.actuator_biasprm[a, 2] < 0.0, name  # -kv * qdot
        assert m.actuator_forcelimited[a] and m.actuator_forcerange[a, 1] > 0.0, name


@pytest.mark.parametrize("xml", MODELS, ids=_name)
def test_no_part_touches_another_in_the_default_stance(xml):
    m, d = _load(xml)
    cfg = _config(_name(xml))
    for joint, q in zip(cfg["joint_order"], cfg["default_joint_pos"]):
        d.qpos[m.jnt_qposadr[mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_JOINT, "a_" + joint)]] = q
    mujoco.mj_forward(m, d)
    own = []
    for c in d.contact[: d.ncon]:
        b1, b2 = m.geom_bodyid[c.geom1], m.geom_bodyid[c.geom2]
        n1 = mujoco.mj_id2name(m, mujoco.mjtObj.mjOBJ_BODY, b1) or ""
        n2 = mujoco.mj_id2name(m, mujoco.mjtObj.mjOBJ_BODY, b2) or ""
        if n1.startswith("a_") and n2.startswith("a_"):
            own.append((n1, n2, round(float(c.dist), 4)))
    assert not own, own
    # parent-child pairs never collide: either MuJoCo's parent filter is on or every such pair is excluded
    assert not (m.opt.disableflags & mujoco.mjtDisableBit.mjDSBL_FILTERPARENT)


@pytest.mark.parametrize("xml", MODELS, ids=_name)
def test_floor_and_feet_have_friction(xml):
    m, _ = _load(xml)
    names = [mujoco.mj_id2name(m, mujoco.mjtObj.mjOBJ_GEOM, g) or "" for g in range(m.ngeom)]
    feet = [g for g, n in enumerate(names) if n.startswith("a_foot")]
    floor = names.index("floor")
    assert feet
    for g in [floor, *feet]:
        assert m.geom_friction[g, 0] >= 0.5, names[g]
        assert m.geom_contype[g] or m.geom_conaffinity[g], names[g]
