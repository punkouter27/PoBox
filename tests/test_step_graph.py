"""envs/step_graph.py: a match step replayed as one CUDA graph does what the eager step does. From the same full state
(env tensors, warp arrays, RNG), one eager step and one replay give the same observation and reward, and the env's
attributes stay the pinned tensors. Needs a CUDA GPU (skipped without one)."""

import os

import pytest

torch = pytest.importorskip("torch")
wp = pytest.importorskip("warp")
pytest.importorskip("mujoco_warp")
if not torch.cuda.is_available():
    pytest.skip("needs a CUDA GPU", allow_module_level=True)

from envs.match import MatchEnv  # noqa: E402
from envs.step_graph import StepGraph  # noqa: E402

pytestmark = pytest.mark.train
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def _env():
    xml = os.path.join(ROOT, "Training", "models", "v3", "kim_vs_matt_spar.xml")
    # The benchmark's rules that change what a step does: daze, fatigue ceiling, pushes, state in the observation.
    e = MatchEnv(
        xml,
        64,
        device="cuda",
        seed=5,
        daze=True,
        obs_state=("matt",),
        hit_push=0.6,
        stagger_v=5.0,
        hurt_full=400.0,
        body_drain=0.0025,
        repeat_decay=0.7,
        combo_w=0.1,
        clinch_w=0.1,
        selfcontact_w=0.02,
        hurt_start=0.2,
        fatigue_ceiling=True,
        realism_for=("matt",),
    )
    e.reset()
    return e


def test_replay_matches_the_eager_step():
    e = _env()
    g = StepGraph(e)
    gen = torch.Generator(device="cuda").manual_seed(1)
    act = lambda: 0.7 * torch.randn(e.N * 2, e.A, device="cuda", generator=gen)
    warp_arrays = lambda: {k: v for k, v in vars(e.dw).items() if isinstance(v, wp.array)}

    def snap():
        torch.cuda.synchronize()
        return (
            {k: v.clone() for k, v in g._tensors().items()},
            {k: wp.clone(v) for k, v in warp_arrays().items()},
            e.rng.get_state(),
        )

    def restore(s):
        for k, v in s[0].items():
            g._get(k).copy_(v)
        for k, v in warp_arrays().items():
            wp.copy(v, s[1][k])
        e.rng.set_state(s[2])
        torch.cuda.synchronize()

    g(act())  # eager, then captured
    g(act())  # the first replay
    assert g.graph is not None and not g.failed and g.captures == 1
    close = 0
    for _ in range(10):
        a = act()
        s0 = snap()
        obs_e, rew_e = (t.clone() for t in e.step(a)[:2])
        restore(s0)
        obs_g, rew_g = (t.clone() for t in g(a)[:2])
        assert all(g._get(k) is t for k, t in g.pins.items())  # still the pinned tensors
        # The MuJoCo Warp solver is not bit-deterministic run to run (atomics); a world may drift, most must not.
        worlds = ((obs_e - obs_g).abs().reshape(e.N, -1).amax(1) < 1e-3) & (
            (rew_e - rew_g).abs().reshape(e.N, -1).amax(1) < 1e-3
        )
        close += int(worlds.sum())
    assert close >= 0.97 * 10 * e.N, f"only {close} of {10 * e.N} world-steps matched"
    assert g.captures == 1  # nothing on the env changed between steps


def test_a_changed_number_recaptures():
    e = _env()
    g = StepGraph(e)
    a = torch.zeros(e.N * 2, e.A, device="cuda")
    g(a)
    g(a)
    e.effort_scale = 0.5
    g(a)
    assert g.captures == 2
