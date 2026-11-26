"""train_distill.py: a privileged teacher and a DAgger student that reads the game's exact 103 numbers, exported as the
trainer exports every brain (ONNX, observation in, actions and value out). A few iterations on the CPU, into tmp_path."""

import json
import os
import sys

import pytest

torch = pytest.importorskip("torch")
pytest.importorskip("mujoco_warp")
onnx = pytest.importorskip("onnx")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRAINING = os.path.join(ROOT, "Training")
if TRAINING not in sys.path:
    sys.path.insert(0, TRAINING)

pytestmark = pytest.mark.train


def test_student_from_teacher_keeps_the_first_inputs():
    from ppo import PPO, PPOConfig
    from train_distill import student_from_teacher

    teacher = PPO(103 + 13, 21, 4, "cpu", PPOConfig())
    teacher.obs_rms.update(torch.randn(64, 116))
    student = PPO(103, 21, 4, "cpu", PPOConfig())
    student_from_teacher(student, teacher, 103)
    x = torch.randn(5, 103)
    with torch.no_grad():
        teacher.model.actor[0].weight[:, 103:] = 0.0  # with the privileged inputs' weights at 0 the two agree exactly
        want = teacher.model.actor(torch.cat([x, torch.randn(5, 13)], -1))
        assert torch.allclose(student.model.actor(x), want, atol=1e-6)
    assert torch.equal(student.obs_rms.mean, teacher.obs_rms.mean[:103])


def test_a_short_run_exports_a_103_number_student(tmp_path):
    from train_distill import main

    xml = os.path.join(TRAINING, "models", "v2", "matt_bag.xml")
    args = ["--xml", xml, "--device", "cpu", "--num-envs", "4", "--steps", "6", "--teacher-iters", "2"]
    args += ["--student-iters", "3", "--beta-iters", "2", "--student-batch", "64", "--out-dir", str(tmp_path)]
    res = main(args + ["--domain-rand", "--muscle", "hill"])
    assert res["obs_dim"] == 103 and res["privileged"] == 13
    assert res["action_mse"] >= 0.0
    for f in ("latest.onnx", "latest.pt", "teacher_latest.pt", "latest_policy_config.json", "distill.csv"):
        assert (tmp_path / f).exists(), f
    model = onnx.load(str(tmp_path / "latest.onnx"))
    dims = [d.dim_value for d in model.graph.input[0].type.tensor_type.shape.dim]
    assert dims[-1] == 103
    assert [o.name for o in model.graph.output] == ["actions", "value"]
    cfg = json.loads((tmp_path / "latest_policy_config.json").read_text(encoding="utf-8"))
    assert cfg["observation_size"] == 103 and cfg["muscle"]["model"] == "hill" and cfg["speed_limit"] is False
