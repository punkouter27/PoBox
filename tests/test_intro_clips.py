"""The walk-on moves (Assets/_Project/Data/Resources/IntroClips.bytes, written by Training/tools/intro_clips.py) on every
shipped boxer's body, as the game puts them there (IntroMoves.cs): the root scaled by the legs, the lower sole on the floor.

They are played, not simulated, so what has to hold is only that they make sense on each body: the boxer's own joints,
in the same order, angles mostly inside its own limits (the game holds the rest at the limit), the pelvis at a
standing height and within a step of where it started, and the clip starting where the game turns it from. A
celebration (celebrate_*) gives only its arms and trunk, so only its angles are checked.
"""

import glob
import json
import os

import pytest

np = pytest.importorskip("numpy")
mujoco = pytest.importorskip("mujoco")

import intro_clips as I  # noqa: E402  (Training/tools)
from retarget_clips import place  # noqa: E402

pytestmark = pytest.mark.physics

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "Training", "models", "v3")
CONFIGS = sorted(glob.glob(os.path.join(ROOT, "Assets", "_Project", "Agents", "Boxers", "*", "config.json")))


@pytest.fixture(scope="module")
def clips():
    return I.read(I.OUT)


def test_file_is_what_the_exporter_says(clips):
    fps, order, by_name = clips
    assert fps == I.FPS
    assert sorted(by_name) == sorted({**I.CLIPS, **I.CELEBRATIONS})
    for name, rows in by_name.items():
        assert rows.shape == (int(I.WINDOW * I.FPS), 7 + len(order)), name
        assert np.isfinite(rows).all(), name
        assert np.allclose(np.linalg.norm(rows[:, 3:7], axis=1), 1.0, atol=1e-4), name
        # Starts at the origin facing +x: the game turns it to the boxer's corner from there.
        assert np.allclose(rows[0, :2], 0.0, atol=1e-6), name
        assert abs(I.heading(rows[0, 3:7].astype(np.float64))) < 1e-4, name


@pytest.mark.parametrize("config", CONFIGS, ids=lambda p: os.path.basename(os.path.dirname(p)))
def test_every_clip_fits_every_boxer(clips, config):
    _, order, by_name = clips
    cfg = json.load(open(config, encoding="utf-8"))
    assert cfg["joint_order"] == order
    xml = os.path.join(MODELS, f"{cfg['name'].lower()}_solo.xml")
    if not os.path.exists(xml):
        pytest.skip(f"no {xml}")
    m = mujoco.MjModel.from_xml_path(xml)
    lower, upper = np.array(cfg["lower"]), np.array(cfg["upper"])
    pelvis = mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_BODY, "a_pelvis")
    for name, rows in by_name.items():
        outside = np.mean((rows[:, 7:] < lower - 1e-3) | (rows[:, 7:] > upper + 1e-3))
        assert outside < 0.08, f"{name}: {outside:.0%} of the angles outside {cfg['name']}'s limits"
        if name.startswith(I.CELEBRATE):
            continue  # only its arms and trunk are played, over the winner's own stance
        qpos = place(m, rows, order)
        d = mujoco.MjData(m)
        heights = []
        for row in qpos:
            d.qpos[:28] = row
            mujoco.mj_kinematics(m, d)
            heights.append(d.xpos[pelvis][2])
        stand = cfg["stand_height"]
        assert 0.6 * stand < min(heights) and max(heights) < 1.15 * stand, (
            f"{name} on {cfg['name']}: pelvis {min(heights):.2f}..{max(heights):.2f} m, stands at {stand:.2f}"
        )
        travel = np.linalg.norm(qpos[:, :2] - qpos[0, :2], axis=1).max()
        assert travel < 0.6, f"{name} on {cfg['name']}: walks {travel:.2f} m"
