"""Does a tired boxer's guard drop (2026-10-30, item 50)? The game's brains in C MuJoCo (tools/exam.py's ring, 3.5.0).

    PYTHONPATH=.mj350 python tools/tired_guard.py --only matt sokol nick grandma --seconds 20 --seeds 3

One boxer (the subject) boxes the game's brain of an opponent with its reserve held at a level all the time, three ways:
    game  its drives tire as the game's do at that reserve, and its brain reads that reserve (config obs_state)
    body  its drives tire, its brain is told 1.0: what the physics alone does to the guard
    mind  its drives are fresh, its brain is told the level: what the brain does about being tired
Measured on the subject while neither glove moves faster than 1.5 m/s (not punching): how far the gloves sit under its
head (cm, the mean of both), the share of such steps with both gloves above its chest (torso capsule's top), and the
head shots it takes a minute. The brains are checkpoints/onnx/match_NAME.pt (tools/onnx_to_pt.py --obs-state from
Assets/Boxers/NAME/match.onnx); the rules (obs_state, hit_push, ...) are each boxer's Assets config. No daze: a guard,
not a knockdown, is being measured; a fall puts both back on their marks.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from multiprocessing import Pool

import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(HERE, "tools"))
sys.path.insert(0, HERE)
import exam as E  # noqa: E402
import exam_v2 as V  # noqa: E402

E.load_policy, E.act = V.load_policy, V.act
ASSETS = os.path.join(os.path.dirname(HERE), "Assets", "Boxers")
LEVELS = (1.0, 0.5, 0.3, 0.1)
MODES = ("game", "body", "mind")


def opponent_for(x: str):
    for y in ("zombie", "matt", "nick", "sokol", "kim", "trump", "grandpa", "grandma"):
        if y != x:
            stem, p, q = E.pair_model(x, y)
            if stem:
                return stem, p, q
    raise SystemExit(f"no pair model for {x}")


def job(args):
    x, level, mode, seed, seconds = args
    stem, p, q = opponent_for(x)
    xml = stem + ("_ring.xml" if os.path.exists(stem + "_ring.xml") else "_spar.xml")
    cfg = json.load(open(stem + "_policy_config.json", encoding="utf-8"))
    for e in cfg.get("fighters", [cfg]):
        game = json.load(open(os.path.join(ASSETS, e["name"].title(), "config.json"), encoding="utf-8"))
        e.update({k: game[k] for k in ("speed_limit", "fatigue_j", "obs_state", "hit_push", "body_drain") if k in game})
    ring = E.ExamRing(xml, cfg, seed=seed)
    names = [e["name"] for e in cfg["fighters"]]
    k = names.index(x)
    pols = [V.load_policy(os.path.join(HERE, "checkpoints", "onnx", f"match_{n}.pt"), ring.A) for n in names]
    for i, pol in enumerate(pols):
        pol.state = (lambda i=i: ring.state(i))
    told = level if mode in ("game", "mind") else 1.0
    felt = level if mode in ("game", "body") else 1.0
    pols[k].state = lambda: np.array([told, ring.share[k], ring.share[1 - k]])
    d, dt = ring.d, ring.dt
    head, torso = ring.head[k], ring.torso[k]
    gl = ring.glove[k]
    prev = np.array([d.geom_xpos[g].copy() for g in gl])
    under, above, n_idle, head_taken = 0.0, 0, 0, 0
    place = ring.place
    ring.at_marks = lambda: place([(-0.72, -0.72), (0.72, 0.72)], [np.pi / 4, np.pi / 4 + np.pi], jitter=0.05)   # a seed differs
    ring.at_marks()
    p_ = E.Punches(ring)
    steps = int(seconds / dt)
    for _ in range(steps):
        ring.reserve[k] = felt
        ring.drive([V.act(pols[i], ring.observe(i)) for i in range(2)])
        ring.reserve[k] = felt
        landed, _ = p_.step()
        head_taken += sum(1 for zone, _ in landed[1 - k] if zone == 0)
        now = np.array([d.geom_xpos[g].copy() for g in gl])
        v = np.linalg.norm(now - prev, axis=1) / dt
        prev = now
        if (v < 1.5).all():
            hz = d.geom_xpos[head][2]
            R = d.geom_xmat[torso].reshape(3, 3)
            chest = d.geom_xpos[torso][2] + abs(R[2, 2]) * ring.half[k] + ring.r_torso[k]
            under += hz - now[:, 2].mean()
            above += int((now[:, 2] > chest).all())
            n_idle += 1
        h, up = ring.posture(k)
        h2, up2 = ring.posture(1 - k)
        if h < 0.6 or up < 0.4 or h2 < 0.6 or up2 < 0.4:
            ring.reset()
            p_ = E.Punches(ring)
            prev = np.array([d.geom_xpos[g].copy() for g in gl])
    return dict(boxer=x, opponent=names[1 - k], level=level, mode=mode, seed=seed, idle=n_idle / steps,
                under_cm=100.0 * under / max(1, n_idle), above=above / max(1, n_idle), head_taken_per_min=head_taken / seconds * 60.0)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=["matt", "sokol", "nick", "grandma"])
    ap.add_argument("--seconds", type=float, default=20.0)
    ap.add_argument("--seeds", type=int, default=3)
    ap.add_argument("--workers", type=int, default=2)
    ap.add_argument("--json", default=os.path.join(HERE, "tools", "tired_guard.json"))
    a = ap.parse_args()
    jobs = [(x, lv, md, s, a.seconds) for x in a.only for lv in LEVELS for md in MODES for s in range(1, a.seeds + 1)]
    with Pool(a.workers) as pool:
        res = pool.map(job, jobs, chunksize=1)
    with open(a.json, "w", encoding="utf-8") as fh:
        json.dump(res, fh, indent=1)
    print("gloves under the head when not punching, cm (higher = lower guard) | both gloves above the chest | head shots taken a minute")
    for x in a.only:
        for md in MODES:
            cells = []
            for lv in LEVELS:
                r = [o for o in res if o["boxer"] == x and o["mode"] == md and o["level"] == lv]
                cells.append(f"R{lv:.1f}: {np.mean([o['under_cm'] for o in r]):5.1f} cm {np.mean([o['above'] for o in r]):4.0%} "
                             f"{np.mean([o['head_taken_per_min'] for o in r]):4.1f}/min")
            print(f"{x:8s} {md:5s} | " + " | ".join(cells))


if __name__ == "__main__":
    main()
