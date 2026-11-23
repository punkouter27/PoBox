"""Training runs grid: Training/logs/runs.html, one row per real training run, newest first.

    python benchmark/runs.py                                   rebuild Training/logs/runs.html from the run records
    python benchmark/runs.py --record <run-id> --ended "<how>" [--launcher <script>]
                                                               note this machine and how the run ended, then rebuild

The grid is rebuilt from what the trainers (Training/train_gauntlet.py, train_box.py, train_getup.py,
train_getup_cpu.py) leave in Training/logs for each run name: <run>.args.json (command line, arguments, start of the
latest session), <run>.csv (per iteration: iteration, hours and steps since the session began; a resumed run appends a
new session to the same file) and <run>.status.json; plus checkpoints/<run>/run_config.json (the commit, written by
the gauntlet) and the queue runner's logs/queue.log ("end <run>: exit N | last line"). So a run started by hand, or one
whose hook never fired, still gets a row. What those cannot tell (which machine, how the run ended) comes from
logs/<run>.run_meta.json, which --record writes; Training/tools/runs_hook.py calls it when a trainer ends, crash or
not. Without it those cells read "-".

Steps and Speed are env steps: one step of one ring (gauntlet) or world (box, get-up), i.e. the CSV's steps column
divided by the boxers each world steps for (the box and get-up trainers count every boxer's step). Speed is the run's
own, measured between the first and last CSV row of its latest session, so a resumed run is not credited with the
steps of its earlier sessions. Benchmark is the newest benchmark/history.json result on the same machine from before
the run started (ms per step of all its envs, and env steps/s), for comparison. Stdlib only; core.py next to this file
supplies the machine description.
"""

from __future__ import annotations

import argparse
import csv
import datetime
import html
import json
import os
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
TRAINING = ROOT / "Training"
LOGS = TRAINING / "logs"
CHECKPOINTS = TRAINING / "checkpoints"
OUT = LOGS / "runs.html"
BENCH = HERE / "history.json"
META = "run_meta.json"  # stored as logs/<run>.run_meta.json, beside the trainer's own logs/<run>.*.json
TITLE = "PoBox"
SKIP = {"_benchmark"}  # the Benchmark's own throwaway run (benchmark/workload.py)


# ---------------------------------------------------------------- collect


def _json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def _mtime(path: Path) -> float | None:
    try:
        return path.stat().st_mtime
    except OSError:
        return None


def _stamp(text: str | None) -> float | None:
    """A local time as the trainers write it (%Y-%m-%d %H:%M:%S) or an ISO time, as seconds."""
    if not text:
        return None
    try:
        return datetime.datetime.fromisoformat(text).timestamp()
    except ValueError:
        return None


def _sessions(path: Path) -> list[list[tuple[int, float, int]]]:
    """The CSV's rows as (iteration, hours, steps), split into sessions: steps restart from 0 when a run resumes."""
    sessions: list[list[tuple[int, float, int]]] = []
    try:
        with path.open(newline="", encoding="utf-8", errors="replace") as fh:
            for row in csv.reader(fh):
                try:
                    it, hours, steps = int(row[0]), float(row[1]), int(row[2])
                except (IndexError, ValueError):
                    continue  # the header
                if not sessions or steps <= sessions[-1][-1][2]:
                    sessions.append([])
                sessions[-1].append((it, hours, steps))
    except OSError:
        pass
    return sessions


def _ended_in_queue() -> dict[str, list[tuple[float, str]]]:
    """tools/queue_runner.py's record of every job it ran: run -> [(when, "exit N | last line")]."""
    out: dict[str, list[tuple[float, str]]] = {}
    for log in sorted(LOGS.glob("queue*.log")):
        try:
            text = log.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for when, run, how in re.findall(r"^(\S+ \S+)\s+end\s+(\S+): (.+)$", text, re.M):
            t = _stamp(when)
            if t is not None:
                how = re.sub(r" to [A-Za-z]:[\\/]\S+", "", how.strip())  # drop " to C:/.../checkpoints/<run>"
                out.setdefault(run, []).append((t, how))
    return out


def _ended_in_log(run: str, start: float) -> str | None:
    """The trainer's own console log (logs/<run>.log, written when the queue runs it): its stop line or a traceback."""
    log = LOGS / f"{run}.log"
    if (_mtime(log) or 0) < start:
        return None
    try:
        tail = log.read_text(encoding="utf-8", errors="replace").splitlines()[-40:]
    except OSError:
        return None
    if any(line.startswith("Traceback") for line in tail):
        errors = [line for line in tail if re.match(r"^\w+(Error|Exception)\b", line)]
        return f"crashed: {errors[-1][:150]}" if errors else f"crashed (see logs/{run}.log)"
    stops = [line for line in tail if line.startswith("[stop]")]
    return stops[-1] if stops else None


def _script(a: dict) -> str:
    if "against" in a:
        return "train_gauntlet.py"
    if a.get("trainer") == "cpu":
        return "train_getup_cpu.py"
    if a.get("trainer") == "gpu":
        return "train_getup.py"
    return "train_box.py"


def _short(path: str) -> str:
    p = path.replace("\\", "/")
    return p.split("checkpoints/", 1)[1] if "checkpoints/" in p else p


def _bench_for(host: str | None, before: float) -> dict | None:
    runs = [r for r in _json(BENCH).get("runs", []) if r.get("ok") and r.get("result")]
    if host:
        runs = [r for r in runs if r.get("machine", {}).get("hostname") == host]
    if not runs:
        return None
    stamp = lambda r: datetime.datetime.fromisoformat(r["when"]).timestamp()  # noqa: E731
    earlier = [r for r in runs if stamp(r) <= before]
    return (earlier or runs)[-1]


def _names() -> set[str]:
    """Every run a trainer started: its logs/<run>.args.json, or the checkpoint folder's run_config.json."""
    names = {p.name[: -len(".args.json")] for p in LOGS.glob("*.args.json")}
    names |= {p.parent.name for p in CHECKPOINTS.glob("*/run_config.json")}
    return {n for n in names if n not in SKIP}


def collect() -> list[dict]:
    rows = []
    if not LOGS.is_dir():
        return rows
    queue = _ended_in_queue()
    for run in _names():
        args_path, csv_path = LOGS / f"{run}.args.json", LOGS / f"{run}.csv"
        a = _json(args_path)
        args = a.get("args", {})
        cfg = _json(CHECKPOINTS / run / "run_config.json")
        meta = _json(LOGS / f"{run}.{META}")
        if not a and cfg:  # only the checkpoint folder's record survives
            a = {"argv": cfg.get("argv", [])[1:], "started": cfg.get("written"), "against": cfg.get("against")}
            args = cfg.get("args", {})  # written by the gauntlet, the only trainer that writes run_config.json
        boxers = max(1, len(a.get("fighters") or [1]))
        sessions = _sessions(csv_path)
        last = sessions[-1] if sessions else []
        session_s = last[-1][1] * 3600 if last else 0.0
        latest = max((t for t in (_mtime(csv_path), _mtime(LOGS / f"{run}.status.json")) if t), default=None)
        start = _stamp(a.get("started")) or (latest - session_s if latest else _mtime(args_path)) or 0.0
        recorded = _stamp(meta.get("recorded"))
        this_session = recorded is not None and recorded >= start - 5
        anchor = max(latest or start, start + session_s)  # the last thing the session wrote
        # The hook records right after the trainer returns; a --record typed in later is not the session's length.
        end = min(recorded, anchor + 900) if this_session else anchor

        steps = sum(s[-1][2] for s in sessions) / boxers if sessions else None
        rate = None
        if len(last) >= 2 and last[-1][1] > last[0][1]:
            rate = (last[-1][2] - last[0][2]) / boxers / ((last[-1][1] - last[0][1]) * 3600)
        elif last and last[-1][1] > 0:
            rate = last[-1][2] / boxers / session_s
        per_iter = args.get("steps") or 0
        envs = (
            last[0][2] // (per_iter * boxers)
            if last and per_iter and last[0][2] % (per_iter * boxers) == 0
            else (args.get("num_envs") or "auto")
        )

        resume = args.get("resume") or []
        warm = [_short(p) for p in ([resume] if isinstance(resume, str) else resume) if p]
        resumed = len(sessions) > 1 or any(w.startswith(f"{run}/") for w in warm)

        if this_session and meta.get("ended"):
            ended = meta["ended"]
        else:
            queued = [how for t, how in queue.get(run, []) if t >= start]
            ended = queued[-1] if queued else _ended_in_log(run, start) or "still running or killed"

        notes = []
        if args.get("models"):
            notes.append(args["models"])
        if args.get("max_hours"):
            notes.append(f"max {args['max_hours']:g} h")
        if args.get("more_iters"):
            notes.append(f"+{args['more_iters']} iters")
        if a.get("against"):
            notes.append(f"vs {len(a['against'])}")
        if args.get("handover"):
            notes.append("handover bank")
        stage = args.get("stage") or a.get("mode")

        git = cfg.get("git", {})
        commit = meta.get("commit") or (git.get("commit") or "")[:7] or None
        device = meta.get("device") or ("CPU" if a.get("trainer") == "cpu" or args.get("device") == "cpu" else None)
        rows.append(
            dict(
                run=run,
                start=start,
                wall=max(0.0, end - start),
                ended=ended,
                resumed=resumed,
                sessions=len(sessions),
                iteration=last[-1][0] + 1 if last else None,
                steps=steps,
                rate=rate,
                bench=_bench_for(meta.get("host"), start),
                config=f"{_script(a)}{' --stage ' + stage if stage else ''}",
                notes=notes,
                envs=f"{envs:,}" if isinstance(envs, int) else envs,
                boxers=boxers,
                warm=warm,
                commit=commit,
                dirty=bool(git.get("dirty")),
                host=meta.get("host"),
                gpu=meta.get("gpu"),
                device=device,
                launcher=meta.get("launcher"),
            )
        )
    rows.sort(key=lambda r: r["start"], reverse=True)
    return rows


# ---------------------------------------------------------------- render


def _dur(s: float) -> str:
    h, m = divmod(int(s) // 60, 60)
    return f"{h} h {m:02d} m" if h else f"{m} m"


def _num(n: float | None, digits: int = 0) -> str:
    return "-" if n is None else f"{n:,.{digits}f}"


def _clip(text: str, n: int = 70) -> str:
    return text if len(text) <= n else text[: n - 1].rstrip() + "…"


def render(rows: list[dict]) -> str:
    e = html.escape
    body = []
    for r in rows:
        b = r["bench"]
        if b:
            notes = [f"{b['result']['env_steps_per_s']} env steps/s", e(b["when"][:10])]
            if datetime.datetime.fromisoformat(b["when"]).timestamp() > r["start"]:
                notes.append("taken after the run")
            if not r["host"]:
                notes.append("machine unknown")
            bench = f"{b['result']['step_time_ms']} ms<small>{' · '.join(notes)}</small>"
        else:
            bench = "-<small>no benchmark on this machine</small>"
        ended = e(r["ended"])
        cls = (
            "bad"
            if re.search(r"crash|died|killed|HALT|abort|error|exception|exit(ed:)? [1-9-]", r["ended"], re.I)
            else ""
        )
        steps_note = []
        if r["iteration"]:
            steps_note.append(f"to iteration {r['iteration']:,}")
        if r["sessions"] > 1:
            steps_note.append(f"{r['sessions']} sessions")
        run_note = "resumed" if r["resumed"] else ""
        envs_note = f"<small>{r['boxers']} boxers a world</small>" if r["boxers"] > 1 else ""
        body.append(
            "<tr>"
            f"<td><b>{e(r['run'])}</b>{f'<small>{run_note}</small>' if run_note else ''}</td>"
            f"<td>{datetime.datetime.fromtimestamp(r['start']).strftime('%Y-%m-%d %H:%M')}</td>"
            f'<td class="n">{_dur(r["wall"])}</td>'
            f'<td class="{cls}" title="{ended}">{e(_clip(r["ended"]))}</td>'
            f'<td class="n">{_num(r["steps"])}<small>{e(" · ".join(steps_note))}</small></td>'
            f'<td class="n"><b>{_num(r["rate"])}</b><small>env steps/s</small></td>'
            f'<td class="n">{bench}</td>'
            f"<td>{e(r['config'])}<small>{e(' · '.join(r['notes']))}</small></td>"
            f'<td class="n">{e(str(r["envs"]))}{envs_note}</td>'
            f"<td>{'<br>'.join(e(w) for w in r['warm']) or 'cold'}</td>"
            f'<td class="mono">{e(r["commit"] or "-")}{"<small>uncommitted changes</small>" if r["dirty"] else ""}</td>'
            f"<td>{e(r['host'] or '-')}<small>{e(r['gpu'] or '')}{' · ' + e(r['device']) if r['device'] else ''}"
            f"</small></td>"
            "</tr>"
        )
    latest = rows[0] if rows else None
    prev = next((r for r in rows[1:] if r["rate"]), None) if rows else None
    delta = ""
    if latest and latest["rate"] and prev:
        pct = 100 * (latest["rate"] / prev["rate"] - 1)
        delta = f'<span class="{"up" if pct >= 0 else "down"}">{pct:+.0f}% vs {e(prev["run"])}</span>'
    summary = (
        f'<div class="sum"><div><span>Latest run</span><b>{e(latest["run"])}</b></div>'
        f"<div><span>Speed</span><b>{_num(latest['rate'])} env steps/s</b>{delta}</div>"
        f"<div><span>Wall-clock</span><b>{_dur(latest['wall'])}</b></div>"
        f"<div><span>Runs listed</span><b>{len(rows)}</b></div></div>"
        if latest
        else "<p>No training runs under Training/logs/ yet.</p>"
    )
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
    return f"""<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>{e(TITLE)} training runs</title>
<style>
:root{{--bg:#f5f7f6;--card:#fff;--ink:#18211f;--muted:#5d6b67;--line:#d8e0dd;--accent:#0f6b5c;--bad:#b42318;--up:#1f7a43}}
@media (prefers-color-scheme:dark){{:root{{--bg:#0f1514;--card:#161e1c;--ink:#e2eae7;--muted:#91a19c;--line:#27332f;
--accent:#5cc9b0;--bad:#ff8a7a;--up:#5fcf8a;color-scheme:dark}}}}
body{{margin:0;background:var(--bg);color:var(--ink);font:14px/1.5 "Segoe UI",system-ui,sans-serif;padding:24px 16px}}
h1{{font-size:20px;margin:0 0 4px}} p{{margin:0;color:var(--muted)}} .wrap{{max-width:1500px;margin:auto;display:grid;gap:16px}}
.sum{{display:flex;flex-wrap:wrap;gap:12px 40px;background:var(--card);border:1px solid var(--line);border-radius:8px;padding:14px 18px}}
.sum span{{display:block;color:var(--muted);font-size:12px}} .sum b{{font-size:20px}} .sum .up,.sum .down{{display:block;font-size:12px}}
.up{{color:var(--up)}} .down{{color:var(--bad)}}
.t{{overflow-x:auto;background:var(--card);border:1px solid var(--line);border-radius:8px}}
table{{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}}
th,td{{padding:8px 12px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top;white-space:nowrap}}
th{{font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);position:sticky;top:0;background:var(--card)}}
tr:last-child td{{border-bottom:0}} td.n{{text-align:right}} td small{{display:block;color:var(--muted);font-size:11px}}
td.bad{{color:var(--bad)}} .mono{{font-family:Consolas,monospace}}
</style></head><body><div class="wrap">
<div><h1>{e(TITLE)} training runs</h1><p>Every real training run under Training/logs/, newest first. Rebuilt {now} by
benchmark/runs.py. Steps and Speed are env steps (one step of one ring or world); Speed = env steps/s of the latest
session; Benchmark = the newest benchmark.ps1 result on the same machine before the run (ms per step of all its envs,
and env steps/s).</p></div>
{summary}
<div class="t"><table><thead><tr><th>Run</th><th>Started</th><th>Wall-clock</th><th>Ended</th><th>Steps</th>
<th>Speed</th><th>Benchmark</th><th>Config</th><th>Envs</th><th>Warm start</th><th>Commit</th><th>Machine</th></tr></thead>
<tbody>{"".join(body)}</tbody></table></div></div></body></html>
"""


def build() -> Path:
    LOGS.mkdir(parents=True, exist_ok=True)
    tmp = OUT.with_suffix(".tmp")
    tmp.write_text(render(collect()), encoding="utf-8")
    os.replace(tmp, OUT)
    return OUT


def record(run_id: str, ended: str, launcher: str | None) -> None:
    """Write Training/logs/<run>.run_meta.json: this machine, the commit and how the run ended."""
    sys.path.insert(0, str(HERE))
    import core

    m = core.machine()
    LOGS.mkdir(parents=True, exist_ok=True)
    meta = dict(
        host=m["hostname"],
        gpu=m["gpu"],
        cpu=m["cpu"],
        device="CPU" if os.environ.get("CUDA_VISIBLE_DEVICES") == "-1" else None,
        commit=core.git_commit(),
        ended=ended,
        launcher=launcher,
        recorded=datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
    )
    (LOGS / f"{run_id}.{META}").write_text(json.dumps(meta, indent=1), encoding="utf-8")


def main() -> None:
    p = argparse.ArgumentParser(description="Rebuild Training/logs/runs.html (the training runs grid).")
    p.add_argument("--record", metavar="RUN_ID", help="note this machine and how the run ended first")
    p.add_argument("--ended", default="exited", help="how the run ended, in words")
    p.add_argument("--launcher", help="the script that ran it")
    a = p.parse_args()
    if a.record:
        record(a.record, a.ended, a.launcher)
    print(f"Training runs grid: {build()}")


if __name__ == "__main__":
    main()
