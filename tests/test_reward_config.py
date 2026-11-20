"""The training config's reward penalty weights (training/config/reward.json): the fast tier."""

import json

import pytest

from envs import reward_config as R


def test_every_env_has_every_penalty_and_none_is_negative():
    for env in ("boxing", "footwork", "getup"):
        weights = R.penalties(env)
        assert set(weights) == set(R.KEYS)
        assert all(w >= 0.0 for w in weights.values()), (env, weights)


def test_the_file_sets_the_torque_and_joint_speed_penalties():
    for env in ("boxing", "footwork", "getup"):
        weights = R.penalties(env)
        assert weights["torque_sq"] > 0.0 and weights["joint_vel_sq"] > 0.0, env


def test_without_the_file_the_old_charges_stand(tmp_path):
    weights = R.penalties("boxing", path=str(tmp_path / "missing.json"))
    assert weights == R.DEFAULTS["boxing"]
    assert weights["torque_sq"] == 0.0 and weights["joint_vel_sq"] == 0.0


def test_an_unknown_weight_is_an_error(tmp_path):
    path = tmp_path / "reward.json"
    path.write_text(json.dumps({"boxing": {"torque": 1.0}}), encoding="utf-8")
    with pytest.raises(ValueError, match="unknown penalty"):
        R.penalties("boxing", path=str(path))
