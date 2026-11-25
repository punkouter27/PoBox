"""envs/randomize.py, per-episode domain randomisation (train_box.py --domain-rand): the ranges file, and on one CPU
world batch that each reset draws a new body within the ranges, the drives lag as drawn, and nothing changes without it."""

import json
import os
import sys

import pytest

torch = pytest.importorskip("torch")
pytest.importorskip("mujoco_warp")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRAINING = os.path.join(ROOT, "Training")
if TRAINING not in sys.path:
    sys.path.insert(0, TRAINING)

from envs import randomize as R  # noqa: E402

XML = os.path.join(TRAINING, "models", "v2", "matt_bag.xml")


def test_the_config_file_is_read_and_knobs_override_it(tmp_path):
    r = R.ranges()
    with open(R.PATH, encoding="utf-8") as fh:
        given = json.load(fh)
    assert r.mass == given["mass"] and r.delay_max == given["delay_max"]
    assert R.ranges(mass=0.05, delay_max=1).mass == 0.05
    bad = tmp_path / "dr.json"
    bad.write_text(json.dumps({"gravity": 0.1}), encoding="utf-8")
    with pytest.raises(ValueError, match="unknown"):
        R.ranges(str(bad))
    with pytest.raises(ValueError):
        R.ranges(str(tmp_path / "missing.json"), mass=1.5)


@pytest.mark.train
def test_each_reset_draws_a_body_within_the_ranges():
    from envs.boxing import BoxingEnv

    r = R.ranges(friction=0.3, mass=0.1, kp=0.15, kd=0.2, delay_max=2, dt_jitter=0.05, obs_noise=0.0)
    env = BoxingEnv(XML, 64, device="cpu", cuda_graph=False, domain_rand=r)
    dr = env.dr
    assert dr.mass.shape[0] == 64 and dr.timestep.shape == (64,)
    f_mass = dr.mass / dr.base["mass"].clamp_min(1e-9)
    real = dr.base["mass"] > 0.0
    assert float(f_mass[real].min()) >= 0.9 - 1e-5 and float(f_mass[real].max()) <= 1.1 + 1e-5
    assert float(f_mass[real].std()) > 0.01, "the worlds differ"
    dt = dr.timestep / dr.base["dt"]
    assert float((dt - 1).abs().max()) <= 0.05 + 1e-6
    kp = dr.gain[..., 0] / dr.base["kp"]
    assert float((kp - 1).abs().max()) <= 0.15 + 1e-5
    lags = set(dr.delay.flatten().tolist())
    assert lags <= {0, 1, 2} and len(lags) > 1
    before = dr.f_fric.clone()
    reset = torch.zeros(64, dtype=torch.bool)
    reset[:32] = True
    env._apply_reset(reset)
    assert not torch.equal(dr.f_fric[:32], before[:32]), "a reset world draws again"
    assert torch.equal(dr.f_fric[32:], before[32:]), "the others keep theirs"
    priv = env.privileged()
    assert priv.shape == (64, 13) and torch.isfinite(priv).all()
    for _ in range(3):
        obs, rew = env.step(torch.zeros(64, env.A))[:2]
    assert torch.isfinite(obs).all() and torch.isfinite(rew).all()


@pytest.mark.train
def test_the_drives_get_the_targets_of_delay_steps_ago():
    from envs.boxing import BoxingEnv

    env = BoxingEnv(XML, 6, device="cpu", cuda_graph=False, domain_rand=R.ranges(delay_max=2, obs_noise=0.0))
    dr = env.dr
    dr.delay.copy_(torch.tensor([[0], [1], [2], [0], [1], [2]]))
    env.episode_len.fill_(0.0)
    wants = [torch.full((6, 1, env.A), float(i)) for i in range(4)]
    out = dr.delayed(wants[0])
    assert torch.equal(out, wants[0]), "an episode's first step fills the history"
    env.episode_len.fill_(1.0)
    for i in (1, 2, 3):
        out = dr.delayed(wants[i])
    assert out[:, 0, 0].tolist() == [3.0, 2.0, 1.0, 3.0, 2.0, 1.0]


@pytest.mark.train
def test_without_it_the_model_is_the_files():
    from envs.boxing import BoxingEnv

    env = BoxingEnv(XML, 4, device="cpu", cuda_graph=False)
    assert env.dr is None and env._dr_obs_noise == 0.0
    assert env.mw.body_mass.shape[0] == 1 and env.mw.opt.timestep.shape[0] == 1
