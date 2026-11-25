"""envs/muscle.py, the Hill muscle (train_box.py --muscle hill), and the game's copy of it (Mj/HillMuscle.cs): the same
golden numbers are in the EditMode test HillMuscleTests, so trainer and game run one rule."""

import os
import sys

import pytest

torch = pytest.importorskip("torch")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRAINING = os.path.join(ROOT, "Training")
if TRAINING not in sys.path:
    sys.path.insert(0, TRAINING)

from envs.muscle import HillDrive, MuscleParams, force_velocity, hill_target_scalar, params_from  # noqa: E402

# (want, q, qd, a, kp, F, vmax, lo, hi, scale, params) -> (target, activation); dt 0.02. Also in HillMuscleTests.cs.
GOLDEN = [
    ((0.5, 0.2, 1.0, 0.0, 300, 150, 10, -1.0, 1.5, 1.0), {}, (0.29378235189595436, 0.29194972858044477)),
    (
        (-0.3, 0.4, -6.0, 0.2, 200, 80, 8, -0.5, 2.0, 0.8),
        {"peak": 0.6, "tau_scale": 1.5},
        (0.3973164526145441, -0.23058353388405445),
    ),
    ((0.0, 0.1, 3.0, -0.5, 250, 120, 12, -1.2, 0.9, 1.0), {}, (-0.09963550187926307, -0.35807999305117266)),
    ((1.2, -0.4, 9.0, 0.9, 400, 200, 9, -1.5, 1.5, 0.5), {"fv_ecc": 1.5, "fl_width": 1.2}, (-0.4, 0.9486582880967408)),
]


@pytest.mark.parametrize("args,over,want", GOLDEN)
def test_the_scalar_reference_is_the_golden_numbers(args, over, want):
    target, act = hill_target_scalar(*args, 0.02, MuscleParams(**over))
    assert target == pytest.approx(want[0], abs=1e-12)
    assert act == pytest.approx(want[1], abs=1e-12)


@pytest.mark.parametrize("args,over,want", GOLDEN)
def test_the_batched_drive_is_the_scalar_reference(args, over, want):
    w, q, qd, a, kp, F, vmax, lo, hi, scale = args
    t = lambda x: torch.tensor([[x]])
    drive = HillDrive([MuscleParams(**over)], t(F), t(vmax), t(lo), t(hi), n=1)
    drive.act += a
    out = drive.target(t(w)[None], t(q)[None], t(qd)[None], t(kp)[None], torch.tensor([[scale]]), 0.02)
    assert float(out) == pytest.approx(want[0], abs=1e-5)  # float32 on the GPU
    assert float(drive.act) == pytest.approx(want[1], abs=1e-5)


def test_torque_falls_to_nothing_at_the_speed_limit():
    v = torch.linspace(0.0, 1.0, 11)
    fv = force_velocity(v, 0.25, 1.3)
    assert fv[0] == pytest.approx(1.0) and fv[-1] == pytest.approx(0.0, abs=1e-7)
    assert bool((fv[1:] <= fv[:-1]).all()), "shortening faster is weaker"
    assert float(force_velocity(torch.tensor(-1.0), 0.25, 1.3)) == pytest.approx(1.3), "lengthening is stronger"


def test_activation_follows_a_step_with_its_time_constant():
    p = MuscleParams(tau_s=0.03)
    a = 0.0
    for _ in range(int(round(0.03 / 0.002))):  # one time constant in small steps
        _, a = hill_target_scalar(10.0, 0.0, 0.0, a, 1000.0, 100.0, 10.0, -1.0, 1.0, 1.0, 0.002, p)
    assert a == pytest.approx(1.0 - 1.0 / 2.718281828, abs=1e-3)


def test_an_old_body_is_weaker_and_slower():
    young, old = MuscleParams(), params_from({"model": "hill", "peak": 0.6}, tau_scale=1.8)
    args = (1.0, 0.0, 0.0, 0.0, 300.0, 150.0, 10.0, -1.0, 1.0, 1.0, 0.02)
    assert hill_target_scalar(*args, old)[0] < hill_target_scalar(*args, young)[0]
    assert hill_target_scalar(*args, old)[1] < hill_target_scalar(*args, young)[1]
    with pytest.raises(ValueError):
        params_from(None, peak=0.0)


@pytest.mark.train
def test_the_env_writes_the_muscle_into_the_config_and_drops_the_damping():
    pytest.importorskip("mujoco_warp")
    from envs.boxing import BoxingEnv

    xml = os.path.join(TRAINING, "models", "v2", "matt_bag.xml")
    plain = BoxingEnv(xml, 2, device="cpu", cuda_graph=False, default_rules=True)
    muscled = BoxingEnv(
        xml, 2, device="cpu", cuda_graph=False, default_rules=True, muscle="hill", muscle_over={"peak": 0.8}
    )
    assert plain.hill is None and "muscle" not in plain.cfgs[0] and plain.limited == [True]
    assert muscled.limited == [False], "the force-velocity curve replaces the speed limit's damping"
    assert muscled.cfgs[0]["muscle"]["model"] == "hill" and muscled.cfgs[0]["muscle"]["peak"] == 0.8
    assert muscled.cfgs[0]["speed_limit"] is False
    assert float(muscled.m.dof_damping.sum()) < float(plain.m.dof_damping.sum())
    for _ in range(10):
        obs, rew = muscled.step(torch.zeros(2, muscled.A))[:2]
    assert torch.isfinite(obs).all() and torch.isfinite(rew).all()
    assert float(muscled.hill.act.abs().max()) > 0.0
