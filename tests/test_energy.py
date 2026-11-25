"""The metabolic energy term (train_box.py --energy-w; the reward config's mech_work): sum |torque x joint speed|, its
own weight beside torque^2, off (absent) unless asked for."""

import os
import sys

import pytest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRAINING = os.path.join(ROOT, "Training")
if TRAINING not in sys.path:
    sys.path.insert(0, TRAINING)

from envs import reward_config as RC  # noqa: E402


def test_the_reward_config_has_the_term_and_it_is_off():
    assert "mech_work" in RC.KEYS
    for env in ("boxing", "footwork", "getup"):
        assert RC.penalties(env)["mech_work"] == 0.0
        assert RC.DEFAULTS[env]["mech_work"] == 0.0


@pytest.mark.train
@pytest.mark.parametrize("stage", ["boxing", "footwork"])
def test_energy_w_charges_the_mechanical_work(stage):
    torch = pytest.importorskip("torch")
    pytest.importorskip("mujoco_warp")
    from envs.boxing import BoxingEnv
    from envs.footwork import FootworkEnv

    models = os.path.join(TRAINING, "models", "v2")
    if stage == "boxing":
        make = lambda **kw: BoxingEnv(os.path.join(models, "matt_bag.xml"), 2, device="cpu", cuda_graph=False, **kw)
    else:
        make = lambda **kw: FootworkEnv(
            os.path.join(models, "matt_solo.xml"), 2, device="cpu", cuda_graph=False, disturb=0.0, randomise=0.0, **kw
        )
    assert "work" not in make().TERMS
    env = make(energy_w=1e-3)
    assert "work" in env.TERMS and env.pen["mech_work"] == 1e-3
    r = {}
    tau = torch.tensor([[[2.0, -3.0]]])
    jv = torch.tensor([[[-1.5, -2.0]]])
    env._torque_terms(r, tau, jv)
    assert float(r["work"]) == pytest.approx(-1e-3 * (3.0 + 6.0))
    gen = torch.Generator().manual_seed(0)
    for _ in range(5):
        env.step(0.5 * torch.randn(2 * env.K, env.A, generator=gen))
    charged = env._acc["rt_work"] if stage == "boxing" else env._ft["work"]
    assert float(charged) < 0.0
