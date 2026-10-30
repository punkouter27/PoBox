"""Checks for the realism flags of 2026-10-30 (envs/boxing.py, envs/footwork.py), on the GPU with a few worlds.

    python tools/smoke_realism.py --old PATH     # PATH: a folder holding a package `oldenvs`, a copy of envs/ before the change

1. Flags off: today's FootworkEnv, MatchEnv and GetUpMatchEnv give exactly the old envs' observations and rewards,
   step for step, from the same seed and the same actions (skipped without --old).
2. action_filter: the action the drives get, and the observation's last action, follow a = f a_last + (1 - f) a_policy,
   the numbers MjBoxer.Drive computes (a numpy copy of it is checked against the env).
3. obs_delay: with obs_delay = obs_delay_max = L the observation is the one of L steps ago, except its last action
   (MjBoxer.Observe's ring buffer, in numpy, against the env).
4. escape_w / flail_w / slope / jog: the terms are finite and logged.
"""
from __future__ import annotations

import argparse
import os
import sys

import numpy as np
import torch

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)

SOLO = os.path.join(HERE, "models", "v2", "matt_solo.xml")
PAIR = os.path.join(HERE, "models", "v2", "matt_vs_zombie_spar.xml")
DEVICE, N_WORLDS = "cuda", 32          # --cpu: Warp on the CPU, bit-repeatable (the GPU's contact solver is not)


def run(env, steps: int, seed: int = 3):
    g = torch.Generator(device=env.device).manual_seed(seed)
    out = [env._obs.clone()]
    rews = []
    for _ in range(steps):
        a = torch.randn(env.N * env.K, env.A, generator=g, device=env.device) * 0.8
        o, r, d, t = env.step(a)
        out.append(o.clone())
        rews.append(r.clone())
    return torch.stack(out), torch.stack(rews)


def identical(old_mod, new_mod, cls: str, xml: str, **kw):
    """Old against new, without CUDA graphs (a graph's replay is not bit-repeatable on this GPU: the contact solver's
    atomics). The old env run twice gives the floor the new one is held to."""
    kw = dict(kw, cuda_graph=False)
    runs = [run(m_(xml, N_WORLDS, seed=5, device=DEVICE, **kw), 60) for m_ in (getattr(old_mod, cls), getattr(old_mod, cls), getattr(new_mod, cls))]
    (oa, ra), (oa2, ra2), (ob, rb) = runs
    floor = max((oa - oa2).abs().max().item(), (ra - ra2).abs().max().item())
    do, dr = (oa - ob).abs().max().item(), (ra - rb).abs().max().item()
    first = (oa[:4] - ob[:4]).abs().max().item()
    print(f"  {cls:14s} {os.path.basename(xml):24s} {kw}: old v new max |obs| {do:.3g} |reward| {dr:.3g} "
          f"(first 3 steps {first:.3g}); old v old {floor:.3g}")
    assert first <= max(1e-6, 10 * floor) and max(do, dr) <= max(1e-6, 10 * floor), "flags off must match the old env"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--old", default="")
    ap.add_argument("--cpu", action="store_true", help="part 1 on Warp's CPU device, 4 worlds: exact comparison")
    ap.add_argument("--only-identity", action="store_true")
    args = ap.parse_args()
    global DEVICE, N_WORLDS
    if args.cpu:
        DEVICE, N_WORLDS = "cpu", 4
    from envs import footwork, getup, match  # noqa: F401
    import envs.match as new_match
    import envs.footwork as new_foot

    if args.old:
        sys.path.insert(0, args.old)
        import oldenvs.match as old_match
        import oldenvs.footwork as old_foot
        print("1. flags off against the old envs")
        identical(old_foot, new_foot, "FootworkEnv", SOLO)
        identical(old_match, new_match, "MatchEnv", PAIR, daze=True, obs_state=("matt",))
        identical(old_match, new_match, "MatchEnv", PAIR, obs_delay_max=2, rope_w=0.05, selfcontact_w=0.02)
        identical(old_match, new_match, "GetUpMatchEnv", PAIR)
    if args.only_identity:
        return

    print("2. action filter (MjBoxer.Drive's arithmetic in numpy)")
    f = 0.4
    env = new_match.MatchEnv(PAIR, 8, seed=1, action_filter=f, realism_for=("matt",), cuda_graph=False)
    k_m = env.names.index("matt")
    assert env.act_filter_k[k_m] == f and env.act_filter_k[1 - k_m] == 0.0, env.act_filter_k
    g = torch.Generator(device="cuda").manual_seed(0)
    held = np.zeros((env.N, env.K, env.A))
    for step in range(40):
        a = torch.randn(env.N * env.K, env.A, generator=g, device="cuda") * 2.0
        raw = a.clamp(-env.action_clip, env.action_clip).reshape(env.N, env.K, env.A).cpu().numpy().astype(np.float64)
        ln = env.episode_len.clone()
        env.step(a)
        # MjBoxer.Drive: Action = clamp(raw); if filter > 0: Action = f * applied + (1 - f) * Action; applied = Action.
        filt = np.array([f if k == k_m else 0.0 for k in range(env.K)])[None, :, None]
        want = filt * held + (1.0 - filt) * raw
        applied = env.last_action.cpu().numpy()
        # A world that ended this step has been reset: its last action is zeros again (MjBoxer.ResetToGuard does the same).
        ended = (env.episode_len == 0).cpu().numpy()
        err = np.abs(applied[~ended] - want[~ended]).max() if (~ended).any() else 0.0
        assert err < 1e-5, f"step {step}: filter differs by {err}"
        held = np.where(ended[:, None, None], 0.0, applied)
    print(f"  40 steps: the env's applied action = f last + (1 - f) policy for matt (f {f}), unfiltered for the other; ok")

    print("3. observation delay (MjBoxer.Observe's ring buffer in numpy)")
    L = 3
    env = new_match.MatchEnv(PAIR, 8, seed=2, obs_delay=L, realism_for=("matt",), cuda_graph=False)
    assert env.delay_lo_k[k_m] == L and env.delay_hi_k[k_m] == L and env.delay_hi_k[1 - k_m] == 0
    hist = None
    a0 = 9 + 2 * env.A
    g = torch.Generator(device="cuda").manual_seed(1)
    worst = 0.0
    rows = env.N * env.K
    for step in range(30):
        a = torch.randn(rows, env.A, generator=g, device="cuda")
        o, r, d, t = env.step(a)
        now = type(env)._observe(env, env._geometry()).cpu().numpy()     # on time (noise differs: compare noise-free numbers only)
        fresh = (env.episode_len == 0).cpu().numpy().repeat(env.K)
        if hist is None:
            hist = np.repeat(now[None], L + 1, 0)
        hist = np.concatenate([now[None], hist[:-1]], 0)
        hist[:, fresh] = now[fresh][None]
        lag = np.array([L if (i % env.K) == k_m else 0 for i in range(rows)])
        want = hist[lag, np.arange(rows)].copy()
        want[:, a0:a0 + env.A] = now[:, a0:a0 + env.A]
        got = o.cpu().numpy()
        if step >= L + 1:
            # obs_noise (default 0) is off here, so the numbers are exact.
            worst = max(worst, float(np.abs(got - want)[~fresh].max()) if (~fresh).any() else 0.0)
    assert worst < 1e-5, f"delay differs by {worst}"
    print(f"  30 steps: matt's observation is {L} steps late (last action on time), zombie's on time; worst diff {worst:.2g}; ok")

    print("4. the new terms")
    env = new_match.MatchEnv(PAIR, 64, seed=3, escape_w=0.1, flail_w=1e-4, ropes_start=0.5, default_rules=True)
    run(env, 50)
    s = env.get_stats()
    print("  match: rt_escape %.4f rt_flail %.4f matt_escape %.4f matt_flail %.1f" % (s["rt_escape"], s["rt_flail"], s["matt_escape"], s["matt_flail"]))
    assert all(np.isfinite(v) for v in (s["rt_escape"], s["rt_flail"]))
    env = new_foot.FootworkEnv(SOLO, 64, seed=3, flail_w=1e-4, cmd_fwd_max=2.5, slope_deg=8.0)
    run(env, 50)
    s = env.get_stats()
    tilt = np.degrees(np.arccos(-env.gravity_world[:, 0, 2].cpu().numpy()))
    print("  footwork: rt_flail %.4f, asked %.2f m/s (cmd_fwd_max 2.5), floor slope %.1f-%.1f deg, falls %.2f"
          % (s["rt_flail"], s["speed_asked"], tilt.min(), tilt.max(), s["fall_rate"]))
    assert np.isfinite(s["rt_flail"]) and tilt.max() <= 8.0 + 1e-3
    g = env.mw.opt.gravity.numpy()
    assert g.shape == (64, 3) and abs(np.linalg.norm(g, axis=1).mean() - 9.81) < 1e-3
    print("all checks passed")


if __name__ == "__main__":
    main()
