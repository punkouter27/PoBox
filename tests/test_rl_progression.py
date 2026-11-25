"""The progressive-training upgrades of 2026-11-25: the physical limits, the terrain, the rung ladder, navigation,
momentum-aware turning, and the encoder reuse. The maths and the ladder run in the fast tier; the env and the policy
ones need the trainer's stack and are marked `train`."""

from __future__ import annotations

import json
import math
import os

import pytest

from envs import curriculum, limits

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, "Training", "config", "progression.json")


# ---- physical limits --------------------------------------------------------------------------------
def test_the_defaults_are_the_games_own_body() -> None:
    """Out of the box nothing is stricter than the body the game runs: no limits are made at all."""
    r = limits.PhysRanges.from_config(PATH)
    assert r == limits.PhysRanges()
    assert r.any is False
    for bad in ({"gravity": 1.0}, {"torque_cap": 0.0}, {"act_lag_hz": -1.0}, {"soft_limit": -0.1}):
        with pytest.raises(ValueError):
            limits.PhysRanges.from_config(PATH, **bad)


def test_a_torque_cap_is_never_exceeded() -> None:
    kp = 200.0
    for want in (-3.0, 0.0, 0.4, 5.0):
        q = 0.1
        out = limits.cap_target(want, q, kp, 30.0)
        assert abs(kp * (out - q)) <= 30.0 + 1e-9
    # under the cap nothing changes
    assert limits.cap_target(0.2, 0.1, kp, 1000.0) == pytest.approx(0.2)


def test_the_stop_gives_way_inside_the_last_radians_of_the_range() -> None:
    lo, hi, margin = -0.5, 0.8, 0.1
    assert limits.stop_gain(0.0, lo, hi, margin) == 0.0
    assert limits.stop_gain(hi, lo, hi, margin) == pytest.approx(1.0)
    assert limits.stop_gain(lo, lo, hi, margin) == pytest.approx(1.0)
    assert 0.0 < limits.stop_gain(0.75, lo, hi, margin) < 1.0
    # at the limit the target is the limit: the drive stops pushing into the stop
    assert limits.stop_target(5.0, hi, lo, hi, margin) == pytest.approx(hi)
    assert limits.stop_target(5.0, lo, lo, hi, margin) == pytest.approx(lo)


def test_a_slower_motor_closes_less_of_the_gap_in_one_step() -> None:
    assert limits.lag_alpha(0.0, 0.02) == 1.0
    fast, slow = limits.lag_alpha(20.0, 0.02), limits.lag_alpha(2.0, 0.02)
    assert 0.0 < slow < fast < 1.0


# ---- terrain and navigation --------------------------------------------------------------------------
def test_terrain_classes_are_weighted_and_a_tilt_is_a_unit_vector() -> None:
    r = limits.TerrainRanges.from_config(PATH)
    names = [c.name for c in r.classes]
    assert names == ["flat", "mild", "steep"]
    picks = [limits.pick_class([0.4, 0.4, 0.2], u) for u in (0.0, 0.39, 0.79, 0.99)]
    assert picks == [0, 0, 1, 2]
    mild = limits.TerrainRanges.from_config(PATH, classes="mild")
    assert [c.name for c in mild.classes] == ["mild"]
    every = limits.TerrainRanges.from_config(PATH, classes="all")  # --terrain alone
    assert [c.name for c in every.classes] == ["flat", "mild", "steep"]
    with pytest.raises(ValueError):
        limits.TerrainRanges.from_config(PATH, classes="lunar")
    for deg in (0.0, 5.0, 14.0):
        v = limits.tilt_vector(deg, 0.7)
        assert math.isclose(sum(x * x for x in v), 1.0, abs_tol=1e-9)
        assert v[2] < 0.0
    assert limits.tilt_vector(0.0, 0.0) == pytest.approx((0.0, 0.0, -1.0))


def test_navigation_ranges_and_steering_point_at_the_goal() -> None:
    r = limits.NavRanges.from_config(PATH)
    assert r.obstacles == (1, 3)
    with pytest.raises(ValueError):
        limits.NavRanges.from_config(PATH, obstacles=(3, 1))
    ahead = limits.steer(2.0, 0.0, 0.0, 1.0)  # goal straight ahead, facing it
    assert ahead[0] == pytest.approx(1.0) and ahead[2] == pytest.approx(0.0, abs=1e-6)
    right = limits.steer(0.0, 2.0, 0.0, 2.0)  # goal two metres to the fighter's left
    assert right[2] > 0.0
    behind = limits.steer(-2.0, 0.0, 0.0, 2.0)
    assert behind[0] < 0.0
    # the command never asks for more than a walk can do
    assert all(-0.4 <= v <= 2.0 for v in limits.steer(10.0, 10.0, 0.0, 2.0))


def test_a_yaw_command_ramps_and_the_lean_follows_v_times_w() -> None:
    rate = 0.0
    for _ in range(100):
        rate = limits.yaw_rate_step(rate, 1.0, 12.0, 0.02)
    assert rate == pytest.approx(1.0)
    one = limits.yaw_rate_step(0.0, 1.0, 12.0, 0.02)
    assert one == pytest.approx(0.24)  # 12 rad/s^2 for 0.02 s
    assert limits.lean_offset(0.0, 1.0, 1.0) == 0.0
    assert limits.lean_offset(2.0, 1.0, 1.0) > limits.lean_offset(1.0, 1.0, 1.0) > 0.0


# ---- the rung ladder --------------------------------------------------------------------------------
def test_the_ladder_is_balance_walk_sprint_navigate() -> None:
    rungs = curriculum.ladder(PATH)
    assert [r.name for r in rungs] == ["balance", "walk", "sprint", "navigate"]
    assert rungs[0].promote and not rungs[0].demote  # nothing below balance to fall back to
    assert rungs[-1].promote == {}  # nothing above navigate yet
    assert rungs[2].overrides["cmd_fwd_max"] > rungs[1].overrides["cmd_fwd_max"]  # a sprint asks for more
    assert rungs[3].overrides["nav_share"] > 0.0


def test_promotion_needs_the_hold_and_sets_the_rung() -> None:
    rungs = curriculum.ladder(PATH)
    s = curriculum.Scheduler(rungs, hold=3, ema=0.0)
    good = {"stand_fall_rate": 0.01, "upright": 0.99}
    assert s.update(good, 0)[0] == ""
    assert s.update(good, 1)[0] == ""
    what, over = s.update(good, 2)
    assert what == "promote" and s.rung.name == "walk"
    assert over["walk_share"] == rungs[1].overrides["walk_share"]
    # and it stops at the top
    top = curriculum.Scheduler(rungs, start=len(rungs) - 1, hold=1)
    for i in range(5):
        assert top.update({}, i)[0] == ""


def test_demotion_needs_the_hold_too_and_a_nan_never_promotes() -> None:
    rungs = curriculum.ladder(PATH)
    s = curriculum.Scheduler(rungs, start=1, hold=2, ema=0.0)
    bad = {"track_err": 0.9}
    assert s.update(bad, 0)[0] == ""
    assert s.update(bad, 1)[0] == "demote"
    assert s.rung.name == "balance"
    s2 = curriculum.Scheduler(rungs, hold=1, ema=0.0)
    assert s2.update({"stand_fall_rate": float("nan"), "upright": 0.99}, 0)[0] == ""


def test_apply_only_touches_knobs_the_env_has() -> None:
    class Env:
        walk_share = 0.5
        cmd_fwd_max = 1.0

    env = Env()
    applied, unknown = curriculum.apply(env, {"walk_share": 0.0, "nav_share": 0.3})
    assert applied == ["walk_share"] and unknown == ["nav_share"]
    assert env.walk_share == 0.0 and isinstance(env.walk_share, float)


def test_apply_tells_the_env_a_rung_moved() -> None:
    """A promotion turns knobs the env must act on (navigation goals, a turn charge): apply must say so."""

    class Env:
        nav_share = 0.0
        calls = 0

        def after_rung(self) -> None:
            self.calls += 1

    env = Env()
    curriculum.apply(env, {"nav_share": 0.35})
    assert env.nav_share == 0.35 and env.calls == 1
    curriculum.apply(env, {"nobody_has_this": 1.0})
    assert env.calls == 1  # nothing applied, nothing to rebuild


def test_a_rung_leaves_the_knobs_given_on_the_command_line_alone() -> None:
    """--nav-share 0.3 with the balance rung: the rung's own 0.0 must not switch navigation off on the way in."""
    from argparse import Namespace

    from envs import upgrades

    class Env:
        walk_share = 0.5
        turn_share = 0.25
        cmd_fwd_max = 1.0
        disturb = 1.0
        nav_share = 0.3
        declared = None

        def declare_rung_terms(self, all_terms: bool = False) -> None:
            self.declared = all_terms

        def after_rung(self) -> None:
            pass

    env = Env()
    args = Namespace(
        curriculum="auto",
        nav_share=0.3,
        nav_obstacles=None,
        sprint_share=None,
        sprint_w=None,
        momentum_w=None,
        lean_w=None,
        turn_accel=None,
        terrain="",
        terrain_bump=None,
        reuse_trunk="",
        freeze_layers=0,
        freeze_until=0,
    )
    upgrades.Progressive(env, [], args)
    assert env.declared is True  # a curriculum run lists every rung's column from iteration 0
    assert env.nav_share == 0.3  # asked for on the command line, so the rung's 0.0 does not win
    assert env.walk_share == 0.0  # asked for by nobody, so the balance rung does


def test_from_args_reads_a_rung_or_auto() -> None:
    rungs = curriculum.ladder(PATH)
    assert curriculum.from_args(rungs, "") is None
    assert curriculum.from_args(rungs, "auto").rung.name == "balance"
    assert curriculum.from_args(rungs, "sprint").rung.name == "sprint"
    with pytest.raises((ValueError, IndexError)):
        curriculum.from_args(rungs, "jog")


def test_the_progression_config_is_complete() -> None:
    with open(PATH, encoding="utf-8") as fh:
        cfg = json.load(fh)
    assert {"physics", "terrain", "nav", "curriculum"} <= set(cfg)
    assert cfg["terrain"]["classes"] and sum(c["weight"] for c in cfg["terrain"]["classes"]) > 0.0
    for r in cfg["curriculum"]["ladder"]:
        for key, cond in r.get("promote", {}).items():
            assert cond[0] in curriculum.OPS, (key, cond)


# ---- the env and the policy (the trainer's stack) ---------------------------------------------------
XML = os.path.join(ROOT, "Training", "models", "v2", "matt_solo.xml")


@pytest.mark.train
def test_the_footwork_rungs_build_and_step() -> None:
    pytest.importorskip("torch")
    pytest.importorskip("mujoco_warp")
    import torch

    from envs.footwork import FootworkEnv

    phys = limits.PhysRanges.from_config(PATH, gravity=0.1, torque_cap=0.7, act_lag_hz=6.0, soft_limit=0.08, rom_w=0.01)
    env = FootworkEnv(
        XML,
        2,
        device="cpu",
        seed=5,
        obs_noise=0.0,
        disturb=0.2,
        randomise=0.0,
        episode_len_s=3.0,
        walk_share=0.6,
        turn_share=0.2,
        nav_share=0.3,
        nav_obstacles=2,
        sprint_share=0.2,
        momentum_w=0.05,
        lean_w=0.2,
        terrain="mild,steep",
        phys=phys,
    )
    for term in ("goal", "sprint", "mom", "tilt", "rom"):
        assert term in env.TERMS, f"{term} is not charged"
    gen = torch.Generator().manual_seed(5)
    for _ in range(40):
        obs, rew = env.step(0.4 * torch.randn(2, env.K, env.A, generator=gen))[:2]
        assert torch.isfinite(obs).all() and torch.isfinite(rew).all()
    s = env.get_stats()
    assert 0.0 <= s["nav_rate"] <= 1.0
    assert s["terrain_mild"] + s["terrain_steep"] == pytest.approx(1.0)
    assert s["gravity_scale"] == pytest.approx(1.0, abs=0.15)  # the batch's mean is the nominal 1
    # every rung's own term is charged (a charge is never positive), and the navigation pay is seen at all
    for term in ("mom", "tilt", "rom"):
        assert s[f"rt_{term}"] <= 0.0
    assert s["rt_rom"] < 0.0, "the range-of-motion charge never fired"
    assert s["rt_tilt"] < 0.0, "the centripetal-lean charge never fired"
    assert s["rt_goal"] != 0.0, "no navigation episode was paid"


@pytest.mark.train
def test_a_promotion_to_the_navigation_rung_turns_navigation_on() -> None:
    """The ladder's last rung sets nav_share on a run that started at balance: the goals must be made then, not at init."""
    pytest.importorskip("torch")
    pytest.importorskip("mujoco_warp")
    import torch

    from envs.footwork import FootworkEnv

    env = FootworkEnv(XML, 2, device="cpu", seed=7, obs_noise=0.0, disturb=0.2, randomise=0.0, episode_len_s=3.0)
    assert env.nav_env is None and "goal" not in env.TERMS
    env.declare_rung_terms(all_terms=True)  # what a curriculum run does at iteration 0: every column is there
    assert "goal" in env.TERMS and "goal" in env._ft
    curriculum.apply(env, {"nav_share": 1.0, "walk_share": 1.0})  # the navigate rung's own knob
    assert env.nav_env is not None and env.nav_share == 1.0
    gen = torch.Generator().manual_seed(7)
    for _ in range(200):
        obs, rew = env.step(0.4 * torch.randn(2, env.K, env.A, generator=gen))[:2]
        assert torch.isfinite(obs).all() and torch.isfinite(rew).all()
    assert env.nav.any(), "no episode was a navigation episode after the promotion"
    assert env.get_stats()["rt_goal"] != 0.0, "the promoted rung paid nothing"


@pytest.mark.train
def test_a_shared_encoder_mirrors_the_rollout_and_can_hold_its_trunk() -> None:
    pytest.importorskip("torch")
    import torch

    import hierarchy
    from envs.symmetry import build_mirror_maps
    from ppo import PPO, PPOConfig

    with open(
        os.path.join(ROOT, "Assets", "_Project", "Agents", "Boxers", "Matt", "config.json"), encoding="utf-8"
    ) as fh:
        order = json.load(fh)["joint_order"]
    obs_dim, act_dim = 103, len(order)
    ppo = PPO(
        obs_dim,
        act_dim,
        8,
        "cpu",
        PPOConfig(steps_per_env=4, epochs=2, minibatches=1, lr=3e-3, shared_encoder=True, mirror_aug=0.5),
    )
    assert ppo.model.shared and hasattr(ppo.model, "trunk")
    assert hierarchy.trainable(ppo)[0] < sum(
        p.numel() for p in PPO(obs_dim, act_dim, 8, "cpu", PPOConfig()).model.parameters()
    )
    ppo.set_symmetry(*build_mirror_maps(order, obs_dim=obs_dim, device="cpu"))

    def rollout() -> None:
        for _ in range(4):
            obs = torch.randn(8, obs_dim)
            ppo.act(obs)
            ppo.record(torch.ones(8), torch.zeros(8), torch.zeros(8))

    rollout()
    stats = ppo.update(torch.randn(8, obs_dim))
    assert stats["mirror_aug"] > 0.0

    # Hold the shared trunk: the heads keep learning, the encoder does not move.
    hierarchy.freeze_trunk(ppo)
    live, total = hierarchy.trainable(ppo)
    assert live < total
    trunk_before = {k: v.clone() for k, v in ppo.model.trunk.named_parameters()}
    head_before = {k: v.clone() for k, v in ppo.model.actor.head.named_parameters()}
    rollout()
    ppo.update(torch.randn(8, obs_dim))
    trunk_after = dict(ppo.model.trunk.named_parameters())
    head_after = dict(ppo.model.actor.head.named_parameters())
    assert all(torch.equal(v, trunk_after[k]) for k, v in trunk_before.items())
    assert any(not torch.equal(v, head_after[k]) for k, v in head_before.items())


@pytest.mark.train
def test_a_trunk_is_copied_between_a_shared_and_a_separate_policy(tmp_path) -> None:
    pytest.importorskip("torch")
    import torch

    import hierarchy
    from ppo import PPO, PPOConfig

    torch.manual_seed(0)
    src = PPO(103, 21, 4, "cpu", PPOConfig(steps_per_env=2, minibatches=1, shared_encoder=True))
    path = str(tmp_path / "shared.pt")
    src.save(path)
    dst = PPO(103, 21, 4, "cpu", PPOConfig(steps_per_env=2, minibatches=1))
    copied = hierarchy.load_trunk(dst, path)
    assert copied, "no encoder layer was copied"
    assert torch.equal(
        hierarchy.linear_layers(dst.model.actor)[0].weight, hierarchy.linear_layers(src.model.trunk)[0].weight
    )
    # the head is the new stage's own problem and is never copied, whatever stage the checkpoint came from
    assert "actor.6" not in copied and "critic.6" not in copied
    same = PPO(103, 21, 4, "cpu", PPOConfig(steps_per_env=2, minibatches=1))
    path2 = str(tmp_path / "separate.pt")
    same.save(path2)
    copied2 = hierarchy.load_trunk(PPO(103, 21, 4, "cpu", PPOConfig(steps_per_env=2, minibatches=1)), path2)
    assert copied2 == ["actor.0", "actor.2", "actor.4", "critic.0", "critic.2", "critic.4"], copied2
