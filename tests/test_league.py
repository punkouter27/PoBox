"""The self-play league (Training/league.py): scoring, PFSP sampling, the pool, frozen opponents. The `train` tier."""

from __future__ import annotations

import os

import pytest

torch = pytest.importorskip("torch")

from league import FrozenPolicy, League, LeagueTrainer, outcome  # noqa: E402
from ppo import PPO, PPOConfig, export_onnx  # noqa: E402

pytestmark = pytest.mark.train
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def _frozen(value: float, dim: int = 4, act: int = 2) -> FrozenPolicy:
    """A policy that always answers `value`."""
    actor = torch.nn.Sequential(torch.nn.Linear(dim, act))
    with torch.no_grad():
        actor[0].weight.zero_()
        actor[0].bias.fill_(value)
    return FrozenPolicy(actor, torch.zeros(dim), torch.ones(dim))


def test_outcome_knockdowns_then_points_then_draws() -> None:
    t = lambda *x: torch.tensor(x)
    b = lambda *x: torch.tensor(x, dtype=torch.bool)
    # ring 0: opponent knocked down; 1: learner knocked down; 2: time-out, learner more hits; 3: time-out, level;
    # 4: still going (no result); 5: both down at once, opponent more hits.
    r_l, r_o = t(0.5, -4.0, 0.1, 0.1, 0.1, -4.0), t(-4.0, 0.5, 0.1, 0.1, 0.1, -4.0)
    hits_l, hits_o = t(0.0, 5.0, 3.0, 2.0, 9.0, 1.0), t(9.0, 0.0, 1.0, 2.0, 0.0, 2.0)
    done, timeout = b(1, 1, 1, 1, 0, 1), b(0, 0, 1, 1, 0, 0)
    win, draw, loss = outcome(r_l, r_o, hits_l, hits_o, done, timeout, fall_penalty=4.0)
    assert win.tolist() == [1, 0, 1, 0, 0, 0]
    assert loss.tolist() == [0, 1, 0, 0, 0, 1]
    assert draw.tolist() == [0, 0, 0, 1, 0, 0]


def test_frozen_policy_matches_ppo_and_adapts_the_observation_width() -> None:
    torch.manual_seed(0)
    ppo = PPO(6, 3, 4, "cpu", PPOConfig())
    for _ in range(3):
        ppo.act(torch.randn(4, 6) * 2.0 + 1.0)  # moves the normaliser off its start
    fz = FrozenPolicy.from_ppo(ppo)
    obs = torch.randn(5, 6)
    want = ppo.model.actor(ppo.obs_rms.normalize(obs, ppo.cfg.obs_clip))
    assert torch.allclose(fz(obs), want, atol=1e-6)
    # a snapshot does not follow the learner afterwards
    with torch.no_grad():
        ppo.model.actor[0].weight.add_(1.0)
    assert torch.allclose(fz(obs), want, atol=1e-6)
    # wider observation: the first numbers; narrower: zeros after them
    assert torch.allclose(fz(torch.cat([obs, torch.randn(5, 3)], -1)), want, atol=1e-6)
    assert fz(obs[:, :4]).shape == (5, 3)


def test_frozen_policy_from_onnx_and_checkpoint_answer_as_the_policy(tmp_path) -> None:
    pytest.importorskip("onnx")
    torch.manual_seed(1)
    ppo = PPO(8, 3, 4, "cpu", PPOConfig())
    ppo.act(torch.randn(4, 8) * 3.0)
    obs = torch.randn(6, 8)
    want = ppo.model.actor(ppo.obs_rms.normalize(obs, ppo.cfg.obs_clip)).detach()
    export_onnx(ppo, str(tmp_path / "p.onnx"), 8)
    ppo.save(str(tmp_path / "p.pt"))
    assert torch.allclose(FrozenPolicy.from_onnx(str(tmp_path / "p.onnx"), "cpu")(obs), want, atol=1e-5)
    assert torch.allclose(FrozenPolicy.from_checkpoint(str(tmp_path / "p.pt"), "cpu")(obs), want, atol=1e-5)


def test_each_ring_takes_its_own_opponents_action() -> None:
    lg = League(6, "cpu", exploit_rings=2, max_size=4)
    for v in (1.0, 2.0, 3.0):
        lg.add(f"c{v:g}", _frozen(v))
    lg.assign[:4] = torch.tensor([2, 0, 1, 2])
    a = lg.act(torch.randn(4, 4))
    assert a[:, 0].tolist() == [3.0, 1.0, 2.0, 3.0]
    assert lg.assign[4:].tolist() == [4, 4]  # the exploiter's rings are never drawn from the pool


def test_pfsp_favours_the_opponent_that_beats_the_learner() -> None:
    lg = League(4, "cpu", max_size=4, pfsp_p=2.0, decay=1.0)
    lg.add("easy", _frozen(0.0))
    lg.add("hard", _frozen(0.0))
    z = torch.zeros(4, 2)
    done = torch.ones(4, 2)
    for _ in range(20):
        lg.assign[:] = torch.tensor([0, 0, 1, 1])
        # the learner knocks "easy" down and is knocked down by "hard"
        rew = torch.tensor([[1.0, -4.0], [1.0, -4.0], [-4.0, 1.0], [-4.0, 1.0]])
        lg.record(rew, done, z, torch.full((4, 2), 99.0))
    w = lg.weights()
    assert w[1] > 20 * w[0] and w[2:].sum() == 0
    draws = lg.sample(4000)
    assert (draws == 1).float().mean() > 0.9
    st = lg.stats()
    assert st["league_size"] == 2 and st["league_score"] == pytest.approx(0.5)


def test_rings_draw_a_new_opponent_only_when_their_episode_ends() -> None:
    lg = League(3, "cpu", max_size=4)
    lg.add("a", _frozen(0.0))
    lg.add("b", _frozen(0.0))
    lg.assign[:] = torch.tensor([1, 1, 1])
    with torch.no_grad():
        lg.res[0, 2] = 100.0  # "a" beats the learner and the learner beats "b": every new draw is "a"
        lg.res[1, 0] = 1000.0
    done = torch.tensor([[1.0, 1.0], [0.0, 0.0], [0.0, 0.0]])
    since = torch.tensor([[99.0, 99.0], [0.0, 99.0], [99.0, 99.0]])
    lg.record(torch.zeros(3, 2), done, torch.zeros(3, 2), since)
    assert lg.assign.tolist() == [0, 1, 1]
    assert lg.hits[1].tolist() == [1.0, 0.0]  # ring 1's learner landed one this step


def test_a_full_pool_drops_its_least_threatening_unprotected_member() -> None:
    lg = League(4, "cpu", max_size=3)
    lg.add("start", _frozen(0.0), protected=True)
    lg.add("weak", _frozen(0.0))
    lg.add("strong", _frozen(0.0))
    with torch.no_grad():
        lg.res[1] = torch.tensor([50.0, 0.0, 0.0])  # the learner always beats "weak"
        lg.res[2] = torch.tensor([0.0, 0.0, 50.0])
    lg.assign[:] = torch.tensor([0, 1, 2, 2])
    lg.add("new", _frozen(0.0))
    assert [m["name"] for m in lg.members] == ["start", "strong", "new"]
    assert lg.res[1].tolist() == [0.0, 0.0, 50.0]  # "strong"'s record moved with it
    assert lg.assign.tolist() == [0, 0, 1, 1]  # "weak"'s ring falls back to the first member until it draws again

    sd = lg.state_dict()
    back = League(4, "cpu", max_size=3)
    back.load_state_dict(sd)
    assert [m["name"] for m in back.members] == ["start", "strong", "new"]
    assert torch.equal(back.res[:3], lg.res[:3])


def test_league_trainer_runs_in_a_spar_ring() -> None:
    pytest.importorskip("mujoco_warp")
    from envs.boxing import BoxingEnv

    xml = os.path.join(ROOT, "Training", "models", "v2", "matt_spar.xml")
    if not os.path.exists(xml):
        pytest.skip("matt_spar.xml not found")
    env = BoxingEnv(xml, num_envs=4, device="cpu", cuda_graph=False, episode_len_s=0.1)
    learner = PPO(env.obs_dim, env.A, env.N, "cpu", PPOConfig(steps_per_env=6, minibatches=1, epochs=1))
    lt = LeagueTrainer(env, learner, "cpu", exploit_share=0.25, snapshot_every=1, exploiter_reset=1, max_size=3)
    assert lt.league.R_pool == 3 and lt.exploiter is not None
    obs = env.reset()
    for _ in range(6):
        o = obs.view(env.N, 2, -1)[:, 0]
        act = lt.actions(obs, learner.act(o))
        assert act.shape == (env.N * 2, env.A)
        obs, rew, done, timeout = env.step(act)
        assert torch.isfinite(rew).all()
        lt.record(rew, done, timeout)
        learner.record(rew.view(env.N, 2)[:, 0], done.view(env.N, 2)[:, 0], timeout.view(env.N, 2)[:, 0])
    learner.update(obs.view(env.N, 2, -1)[:, 0])
    out = lt.end_iter(0, obs)
    assert "exploiter_kl" in out
    assert [m["name"] for m in lt.league.members][-1] in ("self@1", "exploiter@1")
    st = lt.stats()
    assert st["league_size"] >= 2 and 0.0 <= st["league_score"] <= 1.0
