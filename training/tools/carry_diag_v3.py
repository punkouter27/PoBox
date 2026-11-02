"""Why does a boxer fall in the 3 s after its match brain takes the body over from its get-up brain (2026-11-02)?

The exam's get-up test (tools/exam.py getting_up) run as the game runs it (no drain while rising), with every hand-over
measured: reserve, height, tilt, speed and distance to the other boxer at the hand-over; then, over the 3 s, when it fell,
whether the two bodies touched first, and how hard its own brain was pushing (actions at the clip). Each knockdown is run
twice from the same seed: as the exam has it (the other boxer let go and boxing) and with the other boxer left a ghost,
held in its guard, so a fall there is the boxer's own.

    PYTHONPATH=.mj350 python tools/carry_diag_v3.py --name nick --book logs/book_v3t.json --trials 40
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys

import mujoco
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(HERE, "tools"))
sys.path.insert(0, HERE)
import exam as E  # noqa: E402
import exam_v2 as V  # noqa: E402


def handover(ring, k, getup, match, rng_state, opponent_boxes: bool):
    """One knockdown, get-up and hand-over, from rng_state. None if it never got the body to the hand-over."""
    o = 1 - k
    ring.rng.bit_generator.state = rng_state
    rng, dt = ring.rng, ring.dt
    spot = rng.uniform(-1.6, 1.6, 2)
    yaw = rng.uniform(-math.pi, math.pi)
    bearing = yaw + rng.uniform(-0.6, 0.6)
    their = (spot + np.array([math.cos(bearing), math.sin(bearing)]) * rng.uniform(0.9, 2.0)).clip(-2.6, 2.6)
    spots, yaws = [None, None], [0.0, 0.0]
    spots[k], yaws[k] = spot, yaw
    spots[o] = their
    ring.ghost(k, True)
    ring.place(spots, yaws, jitter=0.1)
    ring.held[k] = None
    d = ring.d
    slack = rng.uniform(1.6, 2.6)
    hard = 0.2 + 0.8 * slack / 2.6
    rv = ring.rv[k]
    d.qvel[rv:rv + 2] = rng.uniform(-2.5, 2.5, 2) * hard
    d.qvel[rv + 3:rv + 6] = rng.uniform(-2.5, 2.5, 3) * hard
    stood_for, t, up_at = 0.0, 0.0, -1.0
    full = ring.m.dof_damping.copy()
    mine = np.zeros(ring.m.nv, bool)
    mine[ring.rv[k]:ring.rv[k] + 6] = True
    mine[ring.jv[k]] = True
    ring.m.dof_damping[mine] = full[mine] - (1.0 - ring.getup_share[k]) * ring.sl_extra[mine]
    ring.reserve[k], ring.no_drain[k] = 0.5, True
    face = 0.0
    while t < slack + 8.0 + (E.HANDOVER_S if up_at >= 0.0 else 0.0):
        here = d.xpos[ring.pelvis[k]]
        face = math.atan2(here[1] - their[1], here[0] - their[0])
        ring.pin(o, their, face)
        mujoco.mj_forward(ring.m, d)
        actions = [None, None]
        ring.scale[k] = 0.04 if t < slack else 1.0
        actions[k] = V.act(getup, ring.observe(k))
        ring.drive(actions)
        t += dt
        h, up = ring.posture(k)
        standing = t >= slack and h > 0.88 and up > 0.85
        stood_for = stood_for + dt if standing else 0.0
        if stood_for >= 0.5 and up_at < 0.0:
            up_at = t - slack
        if stood_for >= E.HANDOVER_S:
            break
    ring.m.dof_damping[:] = full
    ring.no_drain[k] = False
    if stood_for < E.HANDOVER_S:
        return None

    # ---- the hand-over
    h0, up0 = ring.posture(k)
    pk, po = d.xpos[ring.pelvis[k]].copy(), d.xpos[ring.pelvis[o]].copy()
    gap = float(np.linalg.norm((pk - po)[:2]))
    fwd = d.xmat[ring.pelvis[k]].reshape(3, 3)[:2, 0]
    to_o = (po - pk)[:2] / max(gap, 1e-6)
    facing = float(fwd @ to_o)
    v0 = float(np.linalg.norm(d.qvel[rv:rv + 3]))
    off_guard = float(np.abs(d.qpos[ring.jq[k]] - ring.default[k]).mean())
    rec = dict(reserve=float(ring.reserve[k]), height=float(h0), tilt=float(up0), gap=gap, facing=facing, speed=v0,
               off_guard=off_guard, fell_at=None, touched_before=False, first_touch=None, clipped=0.0)

    if opponent_boxes:
        ring.ghost(k, False)
    else:
        ring.ghost(k, False)
        ring.ghost(o, True)
    ring.scale[:] = 1.0
    ring.last[:] = 0
    ring.hold(k)
    own = set(ring.own_geoms[k]); theirs = set(ring.own_geoms[o])
    low, clipped, n = 0.0, 0, 0
    for step in range(int(3.0 / dt)):
        acts = [V.act(match[j], ring.observe(j)) for j in range(2)]
        if not opponent_boxes:
            ring.pin(o, their, face)
            acts[o] = None
        clipped += int((np.abs(acts[k]) >= 2.95).sum()); n += len(acts[k])
        ring.drive(acts)
        if rec["first_touch"] is None:
            for c in d.contact[:d.ncon]:
                if (c.geom1 in own and c.geom2 in theirs) or (c.geom2 in own and c.geom1 in theirs):
                    rec["first_touch"] = (step + 1) * dt
                    break
        h, up = ring.posture(k)
        low = low + dt if (h < 0.6 or up < 0.4) else 0.0
        if low > 0.4:
            rec["fell_at"] = (step + 1) * dt - 0.4
            break
    if not opponent_boxes:
        ring.ghost(o, False)
    rec["clipped"] = clipped / max(1, n)
    rec["touched_before"] = rec["fell_at"] is not None and rec["first_touch"] is not None and rec["first_touch"] <= rec["fell_at"]
    ring.ghost(k, False)
    return rec


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", default="nick")
    ap.add_argument("--book", default="logs/book_v3t.json")
    ap.add_argument("--models", default="models/v3")
    ap.add_argument("--against", default="grandma")
    ap.add_argument("--trials", type=int, default=40)
    ap.add_argument("--json", default="")
    args = ap.parse_args()
    V.MODELS = os.path.join(HERE, args.models)
    V.BOOK = os.path.join(HERE, args.book)
    E.HERE = HERE
    E.load_policy, E.act, E.policies, E.MODELS = V.load_policy, V.act, V.policies, V.MODELS
    book = V.policies()
    stem, x, y = E.pair_model(args.against, args.name)
    xml = stem + ("_ring.xml" if os.path.exists(stem + "_ring.xml") else "_spar.xml")
    ring = E.ExamRing(xml, json.load(open(stem + "_policy_config.json", encoding="utf-8")), seed=1)
    k = 0 if x == args.name else 1
    match = [V.load_policy(book[n]["match"], ring.A) for n in (x, y)]
    for i, p in enumerate(match):
        if getattr(p, "obs_state", False):
            p.state = (lambda i=i: ring.state(i))
    getup = V.load_policy(book[args.name]["getup"], ring.A)
    print(f"{args.name} ({book[args.name]['match']}) v {args.against}, {os.path.basename(xml)}, side {k}", flush=True)

    rows = []
    for t in range(args.trials):
        state = ring.rng.bit_generator.state
        a = handover(ring, k, getup, match, state, opponent_boxes=True)
        b = handover(ring, k, getup, match, state, opponent_boxes=False)
        ring.rng.bit_generator.state = state
        ring.rng.uniform(size=16)          # move the stream on for the next knockdown
        if a is None or b is None:
            continue
        rows.append(dict(boxing=a, ghost=b))

    def share(key, f):
        return sum(1 for r in rows if f(r[key])) / max(1, len(rows))

    print(f"{len(rows)} hand-overs")
    for key in ("boxing", "ghost"):
        fell = [r[key] for r in rows if r[key]["fell_at"] is not None]
        print(f"  {key:7s} still up at 3 s: {1 - len(fell) / max(1, len(rows)):.0%}; falls at "
              f"{np.median([f['fell_at'] for f in fell]) if fell else 0:.2f} s (median); touched first in "
              f"{sum(f['touched_before'] for f in fell)} of {len(fell)}")
    both = lambda r: r["boxing"]["fell_at"] is not None
    for label, sel in (("fell", lambda r: both(r)), ("stayed up", lambda r: not both(r))):
        g = [r["boxing"] for r in rows if sel(r)]
        if g:
            m = {k2: float(np.mean([x2[k2] for x2 in g])) for k2 in ("reserve", "height", "tilt", "gap", "facing", "speed", "off_guard", "clipped")}
            print(f"  at the hand-over, {label} ({len(g)}): " + ", ".join(f"{k2} {v:.2f}" for k2, v in m.items()))
    if args.json:
        with open(os.path.join(HERE, args.json), "w", encoding="utf-8") as f:
            json.dump(rows, f, indent=1)


if __name__ == "__main__":
    main()
