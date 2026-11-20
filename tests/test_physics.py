"""The physics tier: the game's MuJoCo (the version pinned in pyproject.toml, which must be the one
Packages/org.mujoco runs) against the C MuJoCo recordings every shipped boxer carries in Assets/Boxers/NAME/.

Each recording (training/tools/footwork_c.py --reference) is one run from the keyframe, a row a control step,
with the controls MuJoCo was given. Replaying those controls on the game's body (training/models/v3, the boxer's
own config.json) must give the same body: every joint within 0.01 rad of the recording over the whole run and the
pelvis within 3 mm in the first second, the bars Unity's own gate uses (MjParityTests). A MuJoCo upgrade that moves
the physics fails here first, before the Unity gates or a browser. (MuJoCo 3.15.0 replays the 3.5.0 recordings to
within 1e-5 rad.)
"""

import glob
import json
import os

import pytest

np = pytest.importorskip("numpy")
mujoco = pytest.importorskip("mujoco")

import footwork_c as F  # noqa: E402  (training/tools; plain MuJoCo + numpy)

pytestmark = pytest.mark.physics

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "training", "models", "v3")
RECORDINGS = sorted(glob.glob(os.path.join(ROOT, "Assets", "Boxers", "*", "reference_*.json")))
JOINT_RAD, PELVIS_M = 0.01, 0.003


def _boxer(path):
    return os.path.basename(os.path.dirname(path))


def _replay(path):
    """The recording's controls, step by step, with its cube throws; the joint and pelvis-height gaps per row."""
    ref = json.load(open(path, encoding="utf-8"))
    name = _boxer(path)
    xml = os.path.join(MODELS, f"{name.lower()}_solo.xml")
    if not os.path.exists(xml):
        pytest.skip(f"no v3 model for {name}")
    solo = F.Solo(xml, json.load(open(os.path.join(os.path.dirname(path), "config.json"), encoding="utf-8")))
    solo.reset(int(ref["kind"]), noise=False, cmd=tuple(ref["command"]))
    m, d = solo.m, solo.d
    np.testing.assert_allclose(d.qpos[solo.jq], np.asarray(ref["start_qpos"])[solo.jq], atol=1e-9)
    substeps = int(round(ref["control_dt"] / m.opt.timestep))
    events, t, joint, height = list(ref["events"]), 0.0, [], []
    for row in ref["rows"]:
        for e in [e for e in events if e["t"] <= t + 1e-9 and e.get("cube", -1) >= 0]:
            q, v = solo.cube_q[e["cube"]], solo.cube_v[e["cube"]]
            d.qpos[q : q + 7], d.qvel[v : v + 6] = e["qpos"], e["qvel"]
            events.remove(e)
        d.ctrl[:] = row["ctrl"]
        for _ in range(substeps):
            mujoco.mj_step(m, d)
        t = row["t"]
        joint.append(float(np.abs(d.qpos[solo.jq] - row["joint_pos"]).max()))
        height.append(abs(float(d.qpos[2]) - row["root_pos"][2]))
    return ref, joint, height


def test_there_are_recordings():
    assert len(RECORDINGS) >= 10, RECORDINGS


@pytest.mark.parametrize("path", RECORDINGS, ids=[f"{_boxer(p)}-{os.path.basename(p)[10:-5]}" for p in RECORDINGS])
def test_replay_matches_the_recording(path):
    ref, joint, height = _replay(path)
    second = int(round(1.0 / ref["control_dt"]))
    worst_joint, worst_height = max(joint), max(height[:second])
    assert worst_joint < JOINT_RAD, f"a joint is {worst_joint:.4f} rad from MuJoCo {ref['mujoco']}'s recording"
    assert worst_height < PELVIS_M, (
        f"the pelvis is {worst_height * 1000:.1f} mm from MuJoCo {ref['mujoco']}'s recording"
    )


def test_the_model_steps_without_blowing_up():
    """Every shipped solo body, keyframe held for 2 s: finite state, no warnings, the pelvis above the floor."""
    for xml in sorted(glob.glob(os.path.join(MODELS, "*_solo.xml"))):
        m = mujoco.MjModel.from_xml_path(xml)
        d = mujoco.MjData(m)
        mujoco.mj_resetDataKeyframe(m, d, 0)
        d.ctrl[:] = [d.qpos[m.jnt_qposadr[m.actuator_trnid[i, 0]]] for i in range(m.nu)]  # hold the keyframe
        for _ in range(int(2.0 / m.opt.timestep)):
            mujoco.mj_step(m, d)
        assert np.isfinite(d.qpos).all() and np.isfinite(d.qvel).all(), xml
        assert d.qpos[2] > 0.2, f"{os.path.basename(xml)}: the pelvis went through the floor"
        assert d.warning[mujoco.mjtWarning.mjWARN_BADQACC].number == 0, xml
