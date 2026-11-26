"""The game's fight log as a bank of real hits, for the trainer's perturbation curriculum (train_box.py --hit-bank).

    python tools/hits_to_bank.py --log <persistentDataPath>/fightlog.ndjson --out logs/hit_bank.npz
    python tools/hits_to_bank.py --log a.ndjson b.ndjson --taker GRANDMA --out logs/hit_bank_grandma.npz

The log (Assets/_Project/Core/Scripts/Runtime/League/FightLog.cs) has one line a bout and, per corner, what it landed:
landedBy (Jab, Cross, Hook, Uppercut, Body), the head and body counts, the total impulse (N s, the game's PUNCH_MASS
x closing speed) and the hardest one. It does not list single punches, so the bank has one row per landed punch with the
punch's kind from landedBy, the part from the kind (Body to the torso, the rest to the head) and a magnitude drawn
around the corner's mean (log-normal, --spread) and held between a quarter of the mean and its peak, then rescaled so
the corner's rows add up to its logged total. The direction is the kind's, in the taker's heading frame (x towards the
puncher, z up): a straight pushes back (-x), a hook sideways, an uppercut back and up, a body shot back and a little
sideways; a hook's side is drawn (the log does not say which hand), and each direction is turned by up to 0.15 rad.

Writes part (M,) int8, dir (M, 3) float32, impulse (M,) float32, kind (M,) int8, taker (M,) str and attacker (M,) str.
A line that is not a bout (blank, broken, or with no corners) is skipped and counted.
"""

from __future__ import annotations

import argparse
import json
import math
import os

import numpy as np

KINDS = ("Jab", "Cross", "Hook", "Uppercut", "Body")
# Each kind's direction in the taker's heading frame (x towards the puncher, y to the taker's left, z up); a hook's and a
# body shot's y is given a side at random.
DIRS = {
    0: (-1.0, 0.0, 0.0),
    1: (-1.0, 0.0, 0.0),
    2: (-0.4, 0.9, 0.0),
    3: (-0.5, 0.0, 0.85),
    4: (-0.9, 0.4, 0.0),
}


def read_log(path: str) -> tuple[list[dict], int]:
    """The bouts of one ndjson log, and how many lines were skipped."""
    bouts, skipped = [], 0
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                rec = json.loads(line)
            except json.JSONDecodeError:
                skipped += 1
                continue
            if (
                not isinstance(rec, dict)
                or not isinstance(rec.get("red"), dict)
                or not isinstance(rec.get("blue"), dict)
            ):
                skipped += 1
                continue
            bouts.append(rec)
    return bouts, skipped


def _direction(kind: int, rng: np.random.Generator) -> np.ndarray:
    x, y, z = DIRS[kind]
    if y:
        y *= 1.0 if rng.random() < 0.5 else -1.0
    turn = rng.uniform(-0.15, 0.15)
    c, s = math.cos(turn), math.sin(turn)
    d = np.array([c * x - s * y, s * x + c * y, z], dtype=np.float64)
    return d / np.linalg.norm(d)


def bank_from_bouts(bouts: list[dict], taker: str = "", spread: float = 0.3, seed: int = 0) -> dict:
    """The rows of every landed punch in the bouts (on `taker` only, when given; names compare without case)."""
    rng = np.random.default_rng(seed)
    part, dirs, imp, kind, takers, attackers = [], [], [], [], [], []
    for rec in bouts:
        for side, other in (("red", "blue"), ("blue", "red")):
            c, t = rec[side], rec[other]
            victim = str(t.get("name", ""))
            if taker and victim.lower() != taker.lower():
                continue
            landed = int(c.get("landed", 0))
            total = float(c.get("impulse", 0.0))
            if landed <= 0 or total <= 0.0:
                continue
            by = [max(0, int(x)) for x in (c.get("landedBy") or [])][: len(KINDS)]
            by += [0] * (len(KINDS) - len(by))
            if sum(by) < landed:  # punches the log could not name: straights
                by[0] += landed - sum(by)
            kinds = [k for k, n in enumerate(by) for _ in range(n)][:landed]
            mean = total / landed
            peak = max(float(c.get("peakImpulse", 0.0)), mean)
            mag = mean * rng.lognormal(0.0, spread, len(kinds)) if spread > 0.0 else np.full(len(kinds), mean)
            mag = np.clip(mag, 0.25 * mean, peak)
            mag = np.clip(mag * (total / mag.sum()), 0.25 * mean, peak)
            for k, m in zip(kinds, mag):
                part.append(1 if k == 4 else 0)
                dirs.append(_direction(k, rng))
                imp.append(m)
                kind.append(k)
                takers.append(victim)
                attackers.append(str(c.get("name", "")))
    return {
        "part": np.asarray(part, dtype=np.int8),
        "dir": np.asarray(dirs, dtype=np.float32).reshape(-1, 3),
        "impulse": np.asarray(imp, dtype=np.float32),
        "kind": np.asarray(kind, dtype=np.int8),
        "taker": np.asarray(takers, dtype=str),
        "attacker": np.asarray(attackers, dtype=str),
    }


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--log", nargs="+", required=True, help="fightlog.ndjson file(s)")
    ap.add_argument("--out", required=True, help="the .npz to write")
    ap.add_argument("--taker", default="", help="only the punches this boxer took (its display name)")
    ap.add_argument("--spread", type=float, default=0.3, help="log-normal sigma of a punch's impulse about its mean")
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args(argv)
    bouts, skipped = [], 0
    for path in args.log:
        b, s = read_log(path)
        bouts += b
        skipped += s
    bank = bank_from_bouts(bouts, args.taker, args.spread, args.seed)
    n = bank["impulse"].shape[0]
    if n == 0:
        raise SystemExit(f"no landed punches in {len(bouts)} bout(s) ({skipped} line(s) skipped); nothing written")
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    np.savez(args.out, **bank)
    counts = ", ".join(f"{name} {int((bank['kind'] == k).sum())}" for k, name in enumerate(KINDS))
    print(
        f"{args.out}: {n} hits from {len(bouts)} bout(s) ({skipped} line(s) skipped): {counts}; "
        f"impulse mean {bank['impulse'].mean():.1f} N s, max {bank['impulse'].max():.1f}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
