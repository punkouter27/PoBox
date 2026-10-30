"""What a run was, written into its checkpoint folder before it trains (2026-10-30, item 49).

    from tools.run_record import write_run_config, brain_rules
    write_run_config(ck_dir, args, envs, extra={...})

run_config.json holds: the command line and every argument (defaults included), the git commit and whether the tree
had uncommitted changes (and which files), the Python / torch / MuJoCo / Warp versions, and each env's settings: every
plain number, flag and string the env object holds (its weights, rules, shares), the fighters and their effective rules
(speed limit, fatigue, the brain's action filter and observation delay) and the model files. Nothing here can stop a run:
a field that cannot be read is written as the error text.
"""
from __future__ import annotations

import json
import os
import platform
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def _git(*cmd: str) -> str:
    try:
        return subprocess.run(["git", *cmd], cwd=HERE, capture_output=True, text=True, timeout=20).stdout.strip()
    except Exception as e:  # pragma: no cover
        return f"(git failed: {e})"


def _versions() -> dict:
    out = {"python": sys.version.split()[0], "platform": platform.platform()}
    for mod in ("torch", "mujoco", "mujoco_warp", "warp", "numpy"):
        try:
            m = __import__(mod)
            out[mod] = getattr(m, "__version__", "?")
        except Exception as e:  # pragma: no cover
            out[mod] = f"(not importable: {e})"
    return out


def brain_rules(env, k: int) -> dict:
    """Fighter k's rules as the game reads them: the boxer config's speed_limit and fatigue_j, and rules_<brain>."""
    return {
        "speed_limit": bool(env.limited[k]),
        "fatigue_j": float(env.fatigue_j_k[k]),
        "fatigue_in_this_stage": bool(env.fatigue_on and bool(env.tires[k])),
        f"rules_{env.brain}": {"action_filter": float(env.act_filter_k[k]), "obs_delay": int(env.delay_lo_k[k]),
                               "obs_delay_max": int(env.delay_hi_k[k])},
    }


def _env_settings(env) -> dict:
    plain = {k: v for k, v in vars(env).items()
             if isinstance(v, (bool, int, float, str)) and not k.startswith("_")}
    for k in ("act_filter_k", "delay_lo_k", "delay_hi_k", "fatigue_j_k", "limited", "names", "TERMS"):
        if hasattr(env, k):
            plain[k] = list(getattr(env, k))
    plain["class"] = type(env).__name__
    plain["rules"] = {n: brain_rules(env, k) for k, n in enumerate(env.names)}
    plain["styles"] = {n: c.get("style", {}) for n, c in zip(env.names, env.cfgs)}
    return plain


def write_run_config(ck_dir: str, args, envs, extra: dict = None) -> str:
    os.makedirs(ck_dir, exist_ok=True)
    status = _git("status", "--porcelain")
    record = {
        "written": time.strftime("%Y-%m-%d %H:%M:%S"),
        "argv": [os.path.basename(sys.argv[0])] + sys.argv[1:],
        "args": {k: v for k, v in vars(args).items()},
        "git": {"commit": _git("rev-parse", "HEAD"), "branch": _git("rev-parse", "--abbrev-ref", "HEAD"),
                "dirty": bool(status), "changed": status.splitlines()[:200]},
        "versions": _versions(),
        "envs": [],
    }
    for e in envs if isinstance(envs, (list, tuple)) else [envs]:
        try:
            record["envs"].append(_env_settings(e))
        except Exception as ex:  # pragma: no cover
            record["envs"].append({"error": repr(ex)})
    record.update(extra or {})
    path = os.path.join(ck_dir, "run_config.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(record, fh, indent=2, default=str)
    print(f"[run] config written to {path}", flush=True)
    return path
