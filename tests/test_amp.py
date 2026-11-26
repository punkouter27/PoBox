"""Adversarial motion priors (Training/amp.py): the features, the discriminator, its PPO hook. The `train` tier."""

from __future__ import annotations

import os

import pytest

torch = pytest.importorskip("torch")
np = pytest.importorskip("numpy")
mujoco = pytest.importorskip("mujoco")

import amp  # noqa: E402
from ppo import PPO, PPOConfig  # noqa: E402

pytestmark = pytest.mark.train
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "Training", "models", "v2")


def _order() -> list:
    import json

    with open(os.path.join(MODELS, "matt_policy_config.json"), encoding="utf-8") as fh:
        return json.load(fh)["joint_order"]


def test_clip_transitions_pair_frames_within_each_clip() -> None:
    frames = np.arange(10, dtype=np.float32)[:, None].repeat(3, 1)
    pairs = amp.clip_transitions(frames, [0, 4, 9])  # clips of 4, 5 and 1 frames
    assert pairs.shape == (3 + 4, 6)
    # never a pair across a clip boundary (3 -> 4, 8 -> 9)
    assert set(map(tuple, pairs[:, [0, 3]].tolist())) == {(i, i + 1) for i in (0, 1, 2, 4, 5, 6, 7)}


def test_walk_clip_features_have_the_feature_set_width() -> None:
    m = mujoco.MjModel.from_xml_path(os.path.join(MODELS, "matt_solo.xml"))
    order = _order()
    for fs, n_ee in (("legs", 2), ("full", 4)):
        frames, starts, states = amp.clip_frames(m, order, amp.WALK_CLIPS, fs, dt=0.02)
        assert frames.shape[1] == amp.frame_dim(len(amp.joint_subset(order, fs)), n_ee)
        assert len(frames) == len(states) and starts[0] == 0
        assert np.isfinite(frames).all()
    # a walk stands on its feet: the root about a leg length up, gravity mostly down the root's -z
    assert 0.7 < float(np.median(frames[:, 0])) < 1.5
    assert float(np.median(frames[:, 3])) < -0.8


def test_place_lowest_puts_a_lying_body_on_the_floor() -> None:
    m = mujoco.MjModel.from_xml_path(os.path.join(MODELS, "matt_solo.xml"))
    order = _order()
    clips, _, _ = amp.load_clips(amp.WALK_CLIPS)
    clip = next(iter(clips.values()))[:5].copy()
    clip[:, 2] = 3.0  # far above the floor, and pitched onto its back
    clip[:, 3:7] = [np.cos(np.pi / 4), 0.0, -np.sin(np.pi / 4), 0.0]
    rows = amp.place_lowest(m, clip, order)
    d = mujoco.MjData(m)
    J = mujoco.mjtObj.mjOBJ_JOINT
    adr = [int(m.jnt_qposadr[mujoco.mj_name2id(m, J, "a_" + n)]) for n in order]
    d.qpos[:7], d.qpos[adr] = rows[0, :7], rows[0, 7:]
    mujoco.mj_kinematics(m, d)
    assert rows[0, 2] < 0.6  # a pelvis lying down (a standing one is about 1 m up)
    floor = mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_GEOM, "floor")

    def floor_dists(drop: float) -> list:
        d.qpos[2] = rows[0, 2] - drop
        mujoco.mj_kinematics(m, d)
        mujoco.mj_collision(m, d)
        return [d.contact[i].dist for i in range(d.ncon) if floor in (d.contact[i].geom1, d.contact[i].geom2)]

    assert all(x > -1e-3 for x in floor_dists(0.0))  # nothing through the floor
    assert floor_dists(0.01)  # and something on it: 1 cm lower, it touches


def test_discriminator_learns_and_rewards_clips_over_the_policy() -> None:
    torch.manual_seed(0)
    expert = (torch.randn(4096, 8) * 0.3 + 1.0).numpy()
    d = amp.AMP(expert, "cpu", hidden=(64, 64), lr=1e-3, buffer=5000, batch=512)
    assert d.step() == {}  # nothing to compare against yet
    d.observe(torch.randn(8000, 8) * 0.3 - 1.0)  # more than the buffer holds: it wraps
    assert d.buf_n == 5000
    for _ in range(200):
        stats = d.step()
    assert {"amp_loss", "amp_gp", "amp_d_clips", "amp_d_policy"} <= set(stats)
    r_real = d.reward(torch.tensor(expert[:512]))
    r_fake = d.reward(torch.randn(512, 8) * 0.3 - 1.0)
    assert ((r_real >= 0) & (r_real <= 1)).all() and ((r_fake >= 0) & (r_fake <= 1)).all()
    assert r_real.mean() > 0.7 > r_fake.mean() + 0.3


def test_ppo_steps_the_discriminator_only_when_attached() -> None:
    def run(attach: bool) -> dict:
        torch.manual_seed(0)
        ppo = PPO(6, 2, 8, "cpu", PPOConfig(steps_per_env=4, epochs=2, minibatches=2))
        disc = amp.AMP(np.random.default_rng(0).normal(size=(256, 4)).astype(np.float32), "cpu", hidden=(16,), batch=32)
        disc.observe(torch.randn(64, 4))
        if attach:
            ppo.set_amp(disc)
        for _ in range(4):
            ppo.act(torch.randn(8, 6))
            ppo.record(torch.randn(8), torch.zeros(8), torch.zeros(8))
        before = [p.clone() for p in disc.net.parameters()]
        stats = ppo.update(torch.randn(8, 6))
        moved = any(not torch.equal(a, b) for a, b in zip(before, disc.net.parameters()))
        return {"stats": stats, "moved": moved}

    off, on = run(False), run(True)
    assert not off["moved"] and not any(k.startswith("amp_") for k in off["stats"])
    assert on["moved"] and "amp_loss" in on["stats"]
    # the policy's own update is the same with or without the discriminator
    assert on["stats"]["policy_loss"] == pytest.approx(off["stats"]["policy_loss"])


def test_env_reads_a_clip_state_as_the_clip_features() -> None:
    pytest.importorskip("mujoco_warp")
    from envs.footwork import FootworkEnv

    env = FootworkEnv(
        os.path.join(MODELS, "matt_solo.xml"),
        2,
        device="cpu",
        seed=1,
        obs_noise=0.0,
        disturb=0.0,
        randomise=0.0,
        cuda_graph=False,
    )
    frames, _, states = amp.clip_frames(env.m, env.cfg["joint_order"], amp.WALK_CLIPS, "full", dt=env.dt)
    reader = amp.EnvReader(env, "full")
    for i in (0, len(states) // 3, len(states) - 1):
        q, v = states[i]
        env.qpos[0] = torch.tensor(q, dtype=torch.float32)
        env.qvel[0] = torch.tensor(v, dtype=torch.float32)
        env._physics_forward()
        assert torch.allclose(reader()[0], torch.tensor(frames[i]), atol=1e-4)

    # The trainer's wrapper pays within [0, w] and feeds the buffer only where a transition counts.
    disc = amp.AMP(amp.clip_transitions(frames, [0]), "cpu", hidden=(32,), batch=64)
    pay = amp.AMPReward(env, disc, reader, weight=0.5, stage="footwork")
    env.reset()
    pay.reset()
    for _ in range(3):
        _, _, done, _ = env.step(torch.zeros(env.N * env.K, env.A))
        r = pay.pay(done)
        assert r.shape == (env.N * env.K,) and (r >= 0).all() and (r <= 0.5).all()
    assert "rt_amp" in pay.stats()
