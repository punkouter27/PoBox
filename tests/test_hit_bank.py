"""tools/hits_to_bank.py (the fight log's hits as a bank) and envs/hit_bank.py (shoves drawn from it, --hit-bank), on
the small synthetic log tests/fixtures/fightlog_sample.ndjson (the FightLog.cs format; one blank and one broken line)."""

import os
import sys

import numpy as np
import pytest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRAINING = os.path.join(ROOT, "Training")
for p in (TRAINING, os.path.join(TRAINING, "tools")):
    if p not in sys.path:
        sys.path.insert(0, p)

import hits_to_bank as H  # noqa: E402

LOG = os.path.join(ROOT, "tests", "fixtures", "fightlog_sample.ndjson")


def test_the_log_is_read_and_a_broken_line_skipped():
    bouts, skipped = H.read_log(LOG)
    assert len(bouts) == 2 and skipped == 1


def test_one_row_per_landed_punch_adding_up_to_the_logged_impulse():
    bouts, _ = H.read_log(LOG)
    bank = H.bank_from_bouts(bouts, seed=3)
    # MATT landed 12 on GRANDMA (144 N s, peak 17.5), GRANDMA 5 (45), NICK 3 hooks on ZOMBIE (45), ZOMBIE none.
    assert bank["impulse"].shape == (20,)
    assert np.allclose(np.linalg.norm(bank["dir"], axis=1), 1.0, atol=1e-5)
    on_grandma = bank["taker"] == "GRANDMA"
    assert on_grandma.sum() == 12
    assert bank["impulse"][on_grandma].sum() == pytest.approx(144.0, rel=0.05)
    assert bank["impulse"][on_grandma].max() <= 17.5 + 1e-4
    assert (bank["part"] == 1).sum() == 3, "the Body punches go to the torso"
    assert (bank["dir"][:, 0] < 0.0).all(), "every punch pushes the taker back"
    hooks = bank["kind"] == 2
    assert np.abs(bank["dir"][hooks, 1]).min() > 0.7
    assert H.bank_from_bouts(bouts, taker="zombie", seed=3)["impulse"].shape == (3,)


def test_the_tool_writes_a_bank_the_env_can_load(tmp_path):
    out = tmp_path / "bank.npz"
    assert H.main(["--log", LOG, "--out", str(out), "--seed", "1"]) == 0
    pytest.importorskip("torch")
    pytest.importorskip("mujoco_warp")
    from envs.hit_bank import load_bank

    bank = load_bank(str(out))
    assert bank["impulse"].shape == (20,) and bank["dir"].shape == (20, 3)
    with pytest.raises(SystemExit):
        H.main(["--log", LOG, "--out", str(tmp_path / "none.npz"), "--taker", "NOBODY"])


@pytest.mark.train
def test_the_shoves_push_and_are_taken_off_again(tmp_path):
    torch = pytest.importorskip("torch")
    pytest.importorskip("mujoco_warp")
    from envs.boxing import BoxingEnv

    out = tmp_path / "bank.npz"
    H.main(["--log", LOG, "--out", str(out)])
    xml = os.path.join(TRAINING, "models", "v2", "matt_bag.xml")
    env = BoxingEnv(xml, 8, device="cpu", cuda_graph=False, push_vel=0.0, hit_bank=str(out), hit_every_s=0.02)
    xfrc = env.hits.xfrc
    for _ in range(3):
        env.step(torch.zeros(8, env.A))
        assert float(xfrc.abs().max()) < 1e-3, "nothing is left on the trunk after the physics"
    assert float(env.hits.count) > 0
    assert float(env.hits.impulse_sum) > 0.0
    env.hits.level.fill_(0.0)
    before = float(env.hits.impulse_sum)
    env.step(torch.zeros(8, env.A))
    assert float(env.hits.impulse_sum) == before, "level 0 (the curriculum's start) is no push"
