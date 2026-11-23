"""The training runs grid's hook: when a trainer's session ends (finished, stopped, crashed), record how it ended.

    if __name__ == "__main__":
        from tools.runs_hook import recorded

        with recorded("train_gauntlet.py"):
            main()

On the way out it finds the run this process trained (the logs/<run>.args.json it wrote, matched by command line) and
runs benchmark/runs.py --record <run> --ended "<how>" --launcher Training/<script>, which notes this machine and the
commit in logs/<run>.run_meta.json and rebuilds Training/logs/runs.html. It never changes what training does and can
never fail it: every error in the hook is printed and dropped, and the trainer's own exception goes on unchanged.
A process that wrote no args.json (bad arguments, a crash while building the envs) and the Benchmark's own throwaway
run (_benchmark) are not recorded.
"""

from __future__ import annotations

import contextlib
import glob
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # Training/
ROOT = os.path.dirname(HERE)
SKIP = {"_benchmark"}  # benchmark/workload.py's run


def _this_run(t0: float) -> str | None:
    """The run whose logs/<run>.args.json this process wrote: newest since t0 with this command line."""
    best = None
    for path in glob.glob(os.path.join(HERE, "logs", "*.args.json")):
        try:
            mtime = os.path.getmtime(path)
            if mtime < t0 - 1:
                continue
            with open(path, encoding="utf-8") as fh:
                argv = json.load(fh).get("argv")
        except (OSError, ValueError, AttributeError):
            continue
        if argv == sys.argv[1:] and (best is None or mtime > best[0]):
            best = (mtime, os.path.basename(path)[: -len(".args.json")])
    return best[1] if best else None


def _record(t0: float, ended: str, launcher: str) -> None:
    try:
        run = _this_run(t0)
        if not run or run in SKIP:
            return
        out = subprocess.run(
            [
                sys.executable,
                os.path.join(ROOT, "benchmark", "runs.py"),
                "--record",
                run,
                "--ended",
                ended,
                "--launcher",
                f"Training/{launcher}",
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=120,
        )
        print(f"[runs] {(out.stdout or out.stderr).strip() or f'runs grid exited {out.returncode}'}", flush=True)
    except BaseException as e:  # noqa: BLE001 - the grid is a side record; it must never fail or hold up training
        print(f"[runs] runs grid not rebuilt: {type(e).__name__}: {e}", flush=True)


@contextlib.contextmanager
def recorded(launcher: str):
    """Run the trainer's main() inside this; on the way out, however it ends, the run is recorded."""
    t0 = time.time()
    ended = "finished"
    try:
        yield
    except KeyboardInterrupt:
        ended = "interrupted (Ctrl+C)"
        raise
    except SystemExit as e:
        if e.code not in (None, 0):
            ended = f"exited: {e.code}"[:200]
        raise
    except BaseException as e:
        ended = f"crashed: {type(e).__name__}: {e}"[:200]
        raise
    finally:
        _record(t0, ended, launcher)
