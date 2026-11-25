"""Progressive curricula for the footwork line: balance -> walk -> sprint -> navigate (train_box.py --curriculum).

The ladder is data (config/progression.json, "curriculum"): each rung names the env knobs it sets and the stats that
promote a run to the next rung (or demote it back). A run starts at the rung the trainer names, and the Scheduler moves
it as the numbers earn it: a condition holds for `hold` updates in a row, on stats smoothed by `ema`, and a rung is held
at least `min_iters` before it may promote. Promotion sets the env's knobs live (FootworkEnv reads walk_share,
cmd_fwd_max, disturb, nav_share and sprint_share every reset), so nothing is restarted and the policy's optimiser state,
noise and value function carry over: the higher rung is fine-tuning, not a fresh run.

    python train_box.py --stage footwork --curriculum walk ...      start (and stay) at walk, never promoting
    python train_box.py --stage footwork --curriculum auto ...      start at balance, climb as the numbers allow
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from typing import Dict, List, Mapping, Sequence, Tuple

PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "config", "progression.json")
OPS = {
    "<=": lambda a, b: a <= b,
    ">=": lambda a, b: a >= b,
    "<": lambda a, b: a < b,
    ">": lambda a, b: a > b,
}
Cond = Mapping[str, Tuple[str, float]]


@dataclass(frozen=True)
class Rung:
    name: str
    overrides: Mapping[str, float] = field(default_factory=dict)
    promote: Cond = field(default_factory=dict)
    demote: Cond = field(default_factory=dict)


def _conds(raw, where: str) -> Dict[str, Tuple[str, float]]:
    out = {}
    for k, cond in (raw or {}).items():
        if not (isinstance(cond, (list, tuple)) and len(cond) == 2 and str(cond[0]) in OPS):
            raise ValueError(f"{where}: {k} needs [op, value] with op one of {sorted(OPS)}, got {cond!r}")
        out[k] = (str(cond[0]), float(cond[1]))
    return out


def load(path: str = PATH) -> dict:
    if not os.path.exists(path):
        raise FileNotFoundError(f"{path}: the progression config (rungs, terrain classes, physical limits) is missing")
    with open(path, encoding="utf-8") as fh:
        return {k: v for k, v in json.load(fh).items() if not k.startswith("_")}


def ladder(path: str = PATH) -> Tuple[Rung, ...]:
    """The rungs, in order."""
    raw = load(path).get("curriculum", {}).get("ladder", [])
    if not raw:
        raise ValueError(f"{path}: curriculum.ladder is empty")
    out = []
    for r in raw:
        name = str(r["name"])
        out.append(
            Rung(
                name=name,
                overrides={k: float(v) for k, v in r.get("overrides", {}).items()},
                promote=_conds(r.get("promote"), f"curriculum.{name}.promote"),
                demote=_conds(r.get("demote"), f"curriculum.{name}.demote"),
            )
        )
    names = [r.name for r in out]
    if len(set(names)) != len(names):
        raise ValueError(f"{path}: curriculum ladder has a repeated rung: {names}")
    return tuple(out)


def settings(path: str = PATH) -> dict:
    """hold, ema and min_iters, with their defaults."""
    c = load(path).get("curriculum", {})
    return {"hold": int(c.get("hold", 5)), "ema": float(c.get("ema", 0.7)), "min_iters": int(c.get("min_iters", 0))}


def holds(conds: Cond, stats: Mapping[str, float]) -> bool:
    """Every condition met (a metric missing from the stats is never met)."""
    for key, (op, value) in conds.items():
        if key not in stats:
            return False
        v = stats[key]
        if v != v:  # NaN never promotes
            return False
        if not OPS[op](float(v), float(value)):
            return False
    return True


class Scheduler:
    """Where on the ladder a run is, and whether this iteration moves it."""

    def __init__(self, rungs: Sequence[Rung], start: int = 0, hold: int = 5, ema: float = 0.7, min_iters: int = 0):
        if not 0 <= start < len(rungs):
            raise IndexError(f"start rung {start} is not on a ladder of {len(rungs)}")
        self.rungs = list(rungs)
        self.i = int(start)
        self.hold, self.ema, self.min_iters = int(hold), float(ema), int(min_iters)
        self.smoothed: Dict[str, float] = {}
        self.entered = 0  # the iteration the current rung began at
        self.good = self.bad = 0  # updates in a row the promote / demote conditions have held
        self.promotions = 0

    @property
    def rung(self) -> Rung:
        return self.rungs[self.i]

    def smooth(self, stats: Mapping[str, float]) -> None:
        """One EMA step over every number a condition reads (0 ema keeps the raw value)."""
        keys = set()
        for r in self.rungs:
            keys |= set(r.promote) | set(r.demote)
        for k in keys:
            if k not in stats or float(stats[k]) != float(stats[k]):
                continue
            v = float(stats[k])
            self.smoothed[k] = v if k not in self.smoothed else self.ema * self.smoothed[k] + (1.0 - self.ema) * v

    def update(self, stats: Mapping[str, float], iteration: int) -> Tuple[str, dict]:
        """Smooth the stats and decide; returns ("promote" | "demote" | "", the env overrides to apply)."""
        self.smooth(stats)
        r = self.rung
        done = iteration - self.entered
        if r.promote and self.i + 1 < len(self.rungs) and done >= self.min_iters:
            self.good = self.good + 1 if holds(r.promote, self.smoothed) else 0
            if self.good >= self.hold:
                self.i, self.entered, self.good, self.bad = self.i + 1, iteration, 0, 0
                self.promotions += 1
                return "promote", dict(self.rung.overrides)
        if r.demote and self.i > 0:
            self.bad = self.bad + 1 if holds(r.demote, self.smoothed) else 0
            if self.bad >= self.hold:
                self.i, self.entered, self.good, self.bad = self.i - 1, iteration, 0, 0
                return "demote", dict(self.rung.overrides)
        return "", {}

    def line(self, stats: Mapping[str, float]) -> str:
        """One line for the hourly status grid: the rung, and how near its next promotion is."""
        r = self.rung
        nxt = self.rungs[self.i + 1].name if self.i + 1 < len(self.rungs) else "top"
        met = [f"{k} {self.smoothed[k]:.3g}{op}{v:g}" for k, (op, v) in r.promote.items() if k in self.smoothed]
        return f"rung {r.name} ({self.i + 1}/{len(self.rungs)}) -> {nxt} [{self.good}/{self.hold}] " + " ".join(met)


def apply(env, overrides: Mapping[str, float]) -> Tuple[List[str], List[str]]:
    """Set each knob the env has; returns (applied, unknown). A rung never stops a run for a knob it does not have."""
    applied, unknown = [], []
    for k, v in overrides.items():
        if hasattr(env, k):
            setattr(env, k, type(getattr(env, k))(v))
            applied.append(k)
        else:
            unknown.append(k)
    if applied:
        hook = getattr(env, "after_rung", None)  # a rung brings a term or a subsystem the env must make (footwork)
        if hook is not None:
            hook()
    return applied, unknown


def from_args(rungs: Sequence[Rung], name: str, iteration: int = 0, path: str = PATH) -> Scheduler | None:
    """The scheduler --curriculum names: "" = none, "auto" = balance, a rung's name = start there and keep climbing."""
    if not name:
        return None
    s = settings(path)
    start = 0 if name in ("auto", "on") else [r.name for r in rungs].index(name)
    sched = Scheduler(rungs, start=start, **s)
    sched.entered = iteration
    return sched
