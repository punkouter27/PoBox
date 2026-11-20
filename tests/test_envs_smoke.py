"""One footwork world on the CPU (MuJoCo Warp + torch), stepped a few times: the reward carries every penalty
of the training config and stays finite. The `train` tier (uv sync --group train)."""

import os

import pytest

torch = pytest.importorskip("torch")
pytest.importorskip("mujoco_warp")

from envs.footwork import FootworkEnv  # noqa: E402

pytestmark = pytest.mark.train
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def test_footwork_steps_and_charges_the_penalties():
    xml = os.path.join(ROOT, "training", "models", "v2", "matt_solo.xml")
    env = FootworkEnv(xml, 1, device="cpu", seed=3, obs_noise=0.0, disturb=0.0, randomise=0.0, episode_len_s=60.0)
    assert {"torque", "jvel"} <= set(env.TERMS)
    for _ in range(3):
        obs, reward = env.step(torch.zeros(1, env.K, env.A) + 0.1)[:2]
        assert torch.isfinite(obs).all() and torch.isfinite(reward).all()
