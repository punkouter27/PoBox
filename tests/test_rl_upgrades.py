"""Tests for the RL locomotion upgrades: bilateral mirror symmetry, xCoM stability, and anti-stall shaping."""

from __future__ import annotations

import json
import os
import sys

import pytest

torch = pytest.importorskip("torch")  # the train tier; CI installs no torch

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRAINING = os.path.join(ROOT, "Training")
if TRAINING not in sys.path:
    sys.path.insert(0, TRAINING)

from envs.symmetry import build_mirror_maps  # noqa: E402
from ppo import PPO, PPOConfig  # noqa: E402


def _matt_config() -> dict:
    path = os.path.join(ROOT, "Assets", "_Project", "Agents", "Boxers", "Matt", "config.json")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


@pytest.mark.parametrize("obs_dim", [100, 103, 106])
@pytest.mark.parametrize("obs_state_tail", [False, True])
def test_mirror_maps_are_exact_involutions(obs_dim: int, obs_state_tail: bool) -> None:
    cfg = _matt_config()
    o_perm, o_sign, a_perm, a_sign = build_mirror_maps(
        cfg["joint_order"], obs_dim=obs_dim, obs_state_tail=obs_state_tail
    )
    assert o_perm.shape == (obs_dim,)
    assert a_perm.shape == (len(cfg["joint_order"]),)

    x = torch.randn(16, obs_dim)
    x_m = x[:, o_perm] * o_sign
    x_mm = x_m[:, o_perm] * o_sign
    assert torch.allclose(x, x_mm, atol=1e-6)

    a = torch.randn(16, len(cfg["joint_order"]))
    a_m = a[:, a_perm] * a_sign
    a_mm = a_m[:, a_perm] * a_sign
    assert torch.allclose(a, a_mm, atol=1e-6)


def test_default_guard_pose_is_bilaterally_symmetric() -> None:
    """Mirroring Matt's shipped default guard pose must reproduce it exactly."""
    cfg = _matt_config()
    _, _, a_perm, a_sign = build_mirror_maps(cfg["joint_order"], obs_dim=103)
    guard = torch.tensor(cfg["default_joint_pos"], dtype=torch.float32)
    mirrored = guard[a_perm] * a_sign
    assert torch.allclose(guard, mirrored, atol=1e-4)


def test_ppo_symmetry_regularisation_runs_and_reduces_asymmetry() -> None:
    cfg = _matt_config()
    obs_dim = 103
    act_dim = len(cfg["joint_order"])
    ppo_cfg = PPOConfig(steps_per_env=4, epochs=5, minibatches=1, lr=5e-3, sym_coef=10.0)
    ppo = PPO(obs_dim, act_dim, num_envs=8, device="cpu", cfg=ppo_cfg)
    maps = build_mirror_maps(cfg["joint_order"], obs_dim=obs_dim, device="cpu")
    ppo.set_symmetry(*maps)
    # Seed a deliberate left/right bias on the output layer so asymmetry is large before the update.
    with torch.no_grad():
        ppo.model.actor[-1].bias.fill_(1.0)

    torch.manual_seed(0)
    batch = []
    for _ in range(ppo_cfg.steps_per_env):
        obs = torch.randn(8, obs_dim)
        batch.append(obs)
        ppo.act(obs)
        ppo.record(torch.zeros(8), torch.zeros(8), torch.zeros(8))

    o_perm, o_sign, a_perm, a_sign = maps
    probe = torch.cat(batch, dim=0)

    def asym() -> float:
        with torch.no_grad():
            n1 = ppo.obs_rms.normalize(probe, ppo_cfg.obs_clip)
            n2 = ppo.obs_rms.normalize(probe[:, o_perm] * o_sign, ppo_cfg.obs_clip)
            m1 = ppo.model.actor(n1)
            m2 = ppo.model.actor(n2)[:, a_perm] * a_sign
            return float((m1 - m2).pow(2).mean().item())

    before = asym()
    stats = ppo.update(torch.randn(8, obs_dim))
    after = asym()

    assert "sym_loss" in stats
    assert stats["sym_loss"] >= 0.0
    assert after < before


def test_boxing_env_xcom_and_stall_terms() -> None:
    """BoxingEnv records xcom and stall reward terms cleanly when enabled."""
    from envs.boxing import BoxingEnv

    xml = os.path.join(TRAINING, "models", "v2", "matt_bag.xml")
    if not os.path.exists(xml):
        pytest.skip("matt_bag.xml not found")
    env = BoxingEnv(
        xml,
        num_envs=2,
        device="cpu",
        cuda_graph=False,
        xcom_w=0.8,
        stall_w=0.1,
        stall_s=0.0,
    )
    assert "xcom" in env.TERMS
    assert "stall" in env.TERMS
    obs, rew, done, timeout = env.step(torch.zeros(env.N * env.K, env.A))
    assert obs.shape == (env.N * env.K, env.obs_dim)
    assert torch.isfinite(rew).all()
    assert env._acc["rt_xcom"].item() <= 0.0
    assert env._acc["rt_stall"].item() < 0.0
