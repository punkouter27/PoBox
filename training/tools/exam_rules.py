"""Run an exam with each brain's own rules of 2026-10-30 mirrored: the action filter and the observation delay
(envs/boxing.py's _take and _delayed; the game's MjBoxer.Drive and Observe). The C exams (tools/footwork_c.py,
tools/exam_v2.py) predate them; this puts them in from outside, so a brain trained with either is examined as it plays.

    PYTHONPATH=.mj350 python tools/exam_rules.py footwork_c --name matt --exam --policy checkpoints/rz_fw_d3_matt/latest.pt
    PYTHONPATH=.mj350 python tools/exam_rules.py exam_v2 --only matt --book logs/book_rz.json ...

Everything after the tool's name is that tool's own arguments. A brain's rules are read from its checkpoint (extra env
"rules", which train_box.py and train_gauntlet.py write from 2026-10-30); a checkpoint without them (every one before,
and tools/onnx_to_pt.py's) is examined exactly as the tool always did. Every episode (a ring placed or reset, a solo
boxer reset) starts the filter from the zero action and the delay's history afresh, as training does.
"""
from __future__ import annotations

import os
import runpy
import sys

import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(HERE, "tools"))
sys.path.insert(0, HERE)


class Rules:
    """One brain's filter and delay, one boxer, numpy: the same arithmetic as envs/boxing.py and MjBoxer."""
    live: list = []

    def __init__(self, rules: dict, A: int, seed: int = 0):
        self.f = float(rules.get("action_filter", 0.0))
        self.lo = int(rules.get("obs_delay", 0))
        self.hi = max(self.lo, int(rules.get("obs_delay_max", 0)))
        self.A, self.rng = A, np.random.default_rng(seed)
        self.on = self.f > 0.0 or self.hi > 0
        self.reset()
        Rules.live.append(self)

    def reset(self) -> None:
        self.applied = np.zeros(self.A)
        self.hist = None

    def obs(self, o: np.ndarray) -> np.ndarray:
        if self.hi <= 0:
            return o
        if self.hist is None:
            self.hist = [o.copy() for _ in range(self.hi + 1)]
            self.lag = self.lo + int(self.rng.integers(self.hi - self.lo + 1))
        else:
            self.hist = [o.copy()] + self.hist[:-1]
        out = self.hist[self.lag].copy()
        a0 = 9 + 2 * self.A
        out[a0:a0 + self.A] = o[a0:a0 + self.A]          # the last action is never late
        return out

    def act(self, a: np.ndarray) -> np.ndarray:
        a = np.clip(np.asarray(a, dtype=float), -3.0, 3.0)
        if self.f > 0.0:
            a = self.f * self.applied + (1.0 - self.f) * a
        self.applied = a
        return a


def rules_of(extra: dict) -> dict:
    for k, v in (extra or {}).get("env", {}).get("rules", {}).items():
        if k.startswith("rules_"):
            return v
    return {}


def reset_all(*_a, **_k) -> None:
    for r in Rules.live:
        r.reset()


def install() -> None:
    import ppo as P
    import exam as E
    import eval_cmujoco as C
    import footwork_c as F

    load = P.PPO.load

    def load_with_rules(self, path, *a, **k):
        extra = load(self, path, *a, **k)
        r = Rules(rules_of(extra), int(extra.get("act_dim", self.model.actor[-1].out_features)))
        self._rules = r if r.on else None
        if r.on:
            print(f"[rules] {os.path.basename(os.path.dirname(path))}/{os.path.basename(path)}: filter {r.f:g}, delay {r.lo}-{r.hi} steps", flush=True)
        return extra
    P.PPO.load = load_with_rules

    act = E.act

    def act_with_rules(ppo, obs):
        r = getattr(ppo, "_rules", None)
        return r.act(act(ppo, r.obs(obs))) if r else act(ppo, obs)
    E.act = act_with_rules

    def after(f):
        def g(*a, **k):
            out = f(*a, **k)
            reset_all()
            return out
        return g
    C.Ring.reset = after(C.Ring.reset)
    E.ExamRing.place = after(E.ExamRing.place)
    F.Solo.reset = after(F.Solo.reset)

    fload = F.load_policy

    def fload_with_rules(path, A):
        f = fload(path, A)
        if not path:
            return f
        import torch
        r = Rules(rules_of(torch.load(path, map_location="cpu", weights_only=False).get("extra", {})), A)
        if not r.on:
            return f
        print(f"[rules] {path}: filter {r.f:g}, delay {r.lo}-{r.hi} steps", flush=True)
        return lambda obs: r.act(f(r.obs(obs)))
    F.load_policy = fload_with_rules


def main() -> None:
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    tool = sys.argv[1].replace(".py", "")
    install()
    sys.argv = [os.path.join(HERE, "tools", tool + ".py")] + sys.argv[2:]
    if tool == "footwork_c":
        import footwork_c as F
        F.main()                         # its globals (load_policy, Solo) are the patched ones
    else:
        runpy.run_path(sys.argv[0], run_name="__main__")


if __name__ == "__main__":
    main()
