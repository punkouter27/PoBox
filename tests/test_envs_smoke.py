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
    xml = os.path.join(ROOT, "Training", "models", "v2", "matt_solo.xml")
    env = FootworkEnv(xml, 1, device="cpu", seed=3, obs_noise=0.0, disturb=0.0, randomise=0.0, episode_len_s=60.0)
    # torque^2, joint speed^2, pelvis angular velocity^2, action rate (jitter), foot slip: each its own term.
    penalties = ("torque", "jvel", "ang", "rate", "slip")
    assert set(penalties) <= set(env.TERMS)
    gen = torch.Generator().manual_seed(3)
    for _ in range(20):
        obs, reward = env.step(0.5 * torch.randn(1, env.K, env.A, generator=gen))[:2]
        assert torch.isfinite(obs).all() and torch.isfinite(reward).all()
    for name in penalties:
        charged = env._ft[name].item()
        assert charged < 0.0, f"{name} was never charged ({charged})"
