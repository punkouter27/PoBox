"""Plain-language status page for the v3 retrain (the queue's v3f_/v3u_/v3m_/exam_ jobs), one self-contained HTML file.

    python tools/v3_status.py [OUT.html]        default ../DOCS/reports/v3_training_status.html

Three charts from the runs' CSVs (the numbers TensorBoard shows under env/), smoothed 0.6 as TensorBoard does:
staying up while walking (footwork falls a minute), getting up (get-up success), landing punches (match hits a second),
plus a timeline estimated from logs/queue.log. Rerun it for the hourly update.
"""
from __future__ import annotations

import base64
import csv
import io
import json
import os
import re
import sys
from datetime import datetime, timedelta

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOGS = os.path.join(HERE, "logs")
ORDER = ["matt", "sokol", "kim", "zombie", "nick", "trump", "grandpa", "grandma"]
COLORS = dict(zip(ORDER, ["#ff7043", "#0077bb", "#009988", "#ee3377", "#33bbee", "#cc3311", "#888888", "#ee7733"]))
STEPS = ["v3f", "exam_v3f", "v3u", "bank_v3", "v3m", "exam_v3m"]
STEP_NAMES = {"v3f": "footwork", "exam_v3f": "footwork exam", "v3u": "get-up", "bank_v3": "get-up endings",
              "v3m": "boxing", "exam_v3m": "boxing exam"}


def series(run, key):
    path = os.path.join(LOGS, f"{run}.csv")
    if not os.path.exists(path):
        return [], []
    xs, ys = [], []
    with open(path, encoding="utf-8") as f:
        for r in csv.DictReader(f):
            try:
                xs.append(int(r["iter"]))
                ys.append(float(r[key]))
            except (KeyError, ValueError, TypeError):
                pass
    return xs, ys


def smooth(ys, w=0.6):
    out, last = [], None
    for y in ys:
        last = y if last is None else last * w + y * (1 - w)
        out.append(last)
    return out


def png(fig):
    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=110, bbox_inches="tight")
    plt.close(fig)
    return base64.b64encode(buf.getvalue()).decode()


def chart(prefix, key, ylabel, title, marks=(), per_boxer_key=False, dashed=None):
    fig, ax = plt.subplots(figsize=(7.5, 3.4))
    drawn = 0
    for name in ORDER:
        xs, ys = series(f"{prefix}_{name}", key.format(name=name) if per_boxer_key else key)
        if xs:
            ax.plot(xs, smooth(ys), color=COLORS[name], lw=2, label=name.capitalize())
            ax.plot(xs, ys, color=COLORS[name], lw=0.6, alpha=0.25)
            if dashed:
                dx, dy = series(f"{prefix}_{name}", dashed.format(name=name))
                if dx:
                    ax.plot(dx, smooth(dy, 0.9), color=COLORS[name], lw=1.4, ls="--")
            drawn += 1
    for y, text, col in marks:
        ax.axhline(y, color=col, ls="--", lw=1)
        ax.text(ax.get_xlim()[1] if drawn else 1, y, " " + text, color=col, va="bottom", ha="right", fontsize=8)
    ax.set_xlabel("training round (iteration)")
    ax.set_ylabel(ylabel)
    ax.set_title(title, fontsize=10, loc="left")
    ax.grid(alpha=0.25)
    if drawn:
        ax.legend(fontsize=8, frameon=False, loc="best")
    else:
        ax.text(0.5, 0.5, "no boxer has reached this step yet", transform=ax.transAxes, ha="center", color="#888")
    return png(fig), drawn


def last(run, key):
    xs, ys = series(run, key)
    return smooth(ys)[-1] if ys else None


def queue_times():
    """run -> (start, end or None) from queue.log."""
    out = {}
    pat = re.compile(r"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\s+(start|end)\s+(\S+?):")
    with open(os.path.join(LOGS, "queue.log"), encoding="utf-8", errors="replace") as f:
        for line in f:
            m = pat.match(line)
            if not m:
                continue
            t = datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S")
            run = m.group(3)
            if m.group(2) == "start":
                out[run] = [t, None]
            elif run in out:
                out[run][1] = t
    return out


def exam(name, kind):
    path = os.path.join(LOGS, f"exam_{kind}_{name}.json")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def exam_text(name):
    """The boxing exam's verdict lines from its log, e.g. ['attack FAIL 0.44 a second ...']."""
    path = os.path.join(LOGS, f"exam_v3m_{name}.log")
    if not os.path.exists(path):
        return []
    lines = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = re.match(r"^\s{4}(footing|attack|guard|chin|getting up|carrying on)\s+(pass|FAIL)\s+(.*)$", line.rstrip())
            if m:
                lines.append(m.groups())
    return lines


def timeline(times, now):
    rows, spans = [], []
    for name in ORDER:
        runs = [f"{s}_{name}" for s in STEPS]
        start = times.get(runs[0], [None])[0]
        end = times.get(runs[-1], [None, None])[1]
        state = "done" if end else ("running" if start else "waiting")
        cur = next((STEP_NAMES[s] for s, r in zip(STEPS, runs) if r in times and times[r][1] is None), None)
        rows.append((name, start, end, state, cur))
        if start and end:
            spans.append(end - start)
    per = sum(spans, timedelta()) / len(spans) if spans else timedelta(hours=2, minutes=20)
    # the running boxer's remainder, then the waiting ones
    eta = now
    for name, start, end, state, _ in rows:
        if state == "running":
            eta = max(now, start + per)
        elif state == "waiting":
            eta += per
    # reruns queued after the eight (v3u2_NAME ... exam_v3m2_NAME): the last four steps, about 0.77 of a boxer
    with open(os.path.join(LOGS, "queue.json"), encoding="utf-8") as f:
        redo = [e["run"] for e in json.load(f) if e["run"].startswith("exam_v3m2_")]
    eta += sum((per * 0.77 for r in redo if not os.path.exists(os.path.join(LOGS, f"{r}.done"))), timedelta())
    return rows, per, eta


def gantt(rows, per, now):
    fig, ax = plt.subplots(figsize=(7.5, 3.2))
    t0 = min((r[1] for r in rows if r[1]), default=now)
    cursor = now
    for i, (name, start, end, state, _) in enumerate(rows):
        y = len(rows) - 1 - i
        if state == "done":
            a, b, alpha, hatch = start, end, 1.0, None
        elif state == "running":
            a, b, alpha, hatch = start, max(now, start + per), 0.55, None
            cursor = b
        else:
            a, b, alpha, hatch = cursor, cursor + per, 0.25, "//"
            cursor = b
        h0 = (a - t0).total_seconds() / 3600
        ax.barh(y, (b - a).total_seconds() / 3600, left=h0, color=COLORS[name], alpha=alpha, hatch=hatch, edgecolor="#444")
        ax.text(h0 - 0.1, y, name.capitalize(), ha="right", va="center", fontsize=8)
    ax.axvline((now - t0).total_seconds() / 3600, color="#111", lw=1.5)
    ax.text((now - t0).total_seconds() / 3600, len(rows) - 0.4, " now", fontsize=8)
    ax.set_yticks([])
    ax.set_xlabel(f"hours since {t0:%H:%M} (solid = done, faded = running, striped = estimated)")
    ax.set_xlim(-1.6, None)
    ax.grid(axis="x", alpha=0.25)
    return png(fig)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "DOCS", "reports", "v3_training_status.html")
    now = datetime.now()
    times = queue_times()
    rows, per, eta = timeline(times, now)
    done = sum(1 for r in rows if r[3] == "done")

    c1, _ = chart("v3f", "falls_per_min", "falls a minute",
                  "1. Staying on its feet: falls a minute while walking and turning (lower is better)")
    c2, _ = chart("v3u", "{name}_up_rate", "share that got up",
                  "2. Getting up: solid = with the practice helping hand, dashed = on its own (higher is better)", dashed="{name}_up_rate_unaided",
                  marks=[(0.8, "exam mark 80%", "#2a7")], per_boxer_key=True)
    c3, _ = chart("v3m", "hits_per_s", "punches landed a second",
                  "3. Boxing: punches landed a second against the other seven (higher is better)",
                  marks=[(0.5, "exam mark 0.5", "#2a7"), (1.2, "training goal 1.2", "#999")])
    g = gantt(rows, per, now)

    cards = []
    for name, start, end, state, cur in rows:
        fw = exam(name, "v3f")
        box = exam_text(name)
        bits = []
        if fw:
            ok = all(fw[k]["pass"] for k in ("stand", "walk", "turn", "joints"))
            bits.append(f"footwork exam <b class={'ok' if ok else 'bad'}>{'PASS' if ok else 'FAIL'}</b>")
        if box:
            fails = [f"{k} ({v})" for k, p, v in box if p == "FAIL"]
            verdict = "PASS" if not fails else "FAIL"
            bits.append(f"boxing exam <b class={'ok' if not fails else 'bad'}>{verdict}</b>"
                        + (": " + "; ".join(fails) if fails else ""))
        if state == "running":
            bits.append(f"now: {cur or 'between steps'}")
        if state == "waiting":
            bits.append("waiting its turn")
        cards.append(f"<tr><td style='color:{COLORS[name]}'><b>{name.capitalize()}</b></td><td>{state}</td><td>{' &middot; '.join(bits)}</td></tr>")

    html = f"""<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>PoBox v3 training status {now:%Y-%m-%d %H:%M}</title>
<style>
 body {{ margin:0; background:#fafafa; color:#222; font:15px/1.55 system-ui,"Segoe UI",sans-serif; }}
 main {{ max-width:860px; margin:0 auto; padding:24px 16px 60px; }}
 h1 {{ font-size:24px; margin:0; }} h2 {{ font-size:18px; margin:32px 0 6px; }}
 .dim {{ color:#777; font-size:13px; }} img {{ width:100%; background:#fff; border:1px solid #e3e3e3; border-radius:6px; }}
 table {{ border-collapse:collapse; width:100%; }} td,th {{ padding:6px 8px; border-bottom:1px solid #e6e6e6; text-align:left; vertical-align:top; font-size:14px; }}
 .ok {{ color:#1a8a4a; }} .bad {{ color:#c62828; }}
 .lv {{ display:grid; grid-template-columns:110px 1fr; gap:2px 10px; font-size:14px; margin-top:8px; }} .lv b {{ color:#555; }}
 .big {{ font-size:17px; background:#fff; border:1px solid #e3e3e3; border-radius:6px; padding:12px 14px; }}
</style></head><body><main>
<h1>Retraining the boxers on their new bodies (v3)</h1>
<p class="dim">Updated {now:%A %d %B, %H:%M}. Rebuilt from the training logs by training/tools/v3_status.py.</p>

<p class="big"><b>{done} of 8 boxers finished.</b> At about {per.total_seconds()/3600:.1f} hours a boxer, the last one should be done around
<b>{eta:%H:%M} {'tomorrow' if eta.date() > now.date() else 'today'}</b>. Keep Unity closed until then; it slows training about five times.</p>

<h2>What is happening</h2>
<p>Each boxer got a new body: a waist that bends 60&deg; forward and 30&deg; back like a person's, and body parts weighed the way
real people's are (heavier thighs, a lighter trunk). Its old brain was learned on the old body, so each boxer goes back to school
in three lessons: <b>walking</b> (30 min), <b>getting up</b> (25 min), then <b>boxing</b> against the seven others (45 min). After the
walking and boxing lessons it sits an exam in the same physics the game uses. Nothing changes in the game until a boxer passes.</p>

<h2>Timeline</h2>
<img src="data:image/png;base64,{g}" alt="timeline">
<table style="margin-top:10px">{''.join(cards)}</table>

<h2>The three numbers that matter</h2>
<img src="data:image/png;base64,{c1}" alt="falls a minute">
<div class="lv"><b>Toddler</b><span>Line near the floor = the boxer doesn't fall over.</span>
<b>Child</b><span>Every minute of walking and turning, how often it tripped. The new body has to learn to balance again; a flat line near zero means it has.</span>
<b>Adult</b><span>env/falls_per_min of the footwork run, all commands mixed (stand, walk, turn, shoves). The footwork exam then checks stand/walk/turn separately in C MuJoCo 3.5.0.</span></div>

<img src="data:image/png;base64,{c2}" alt="get-up rate" style="margin-top:22px">
<div class="lv"><b>Toddler</b><span>Higher = it gets back up after falling.</span>
<b>Child</b><span>Out of every knockdown in practice, the share it stood up from on its own. The exam wants 8 in 10 within 8 seconds.</span>
<b>Adult</b><span>env/NAME_up_rate in the get-up run (standing still 1.5 s within the episode), trained against a frozen partner; the hand-over bank then saves its last standing poses for the boxing lesson.</span></div>

<img src="data:image/png;base64,{c3}" alt="punches landed a second" style="margin-top:22px">
<div class="lv"><b>Toddler</b><span>Higher = it hits the other boxer more.</span>
<b>Child</b><span>Punches that actually land each second. The exam asks for at least one every two seconds (0.5), and fast ones (4 m/s).</span>
<b>Adult</b><span>env/hits_per_s over the gauntlet (all seven opponents mixed). The run stops early if it holds 1.2/s with few falls and varied punches; otherwise at 45 min.</span></div>

<h2>How it compares</h2>
<ul>
<li><b>Walking: better than expected.</b> Matt and Sokol learned to stand and walk on the new body within the half hour, about 0.1 falls a minute, and both passed every line of the walking exam.</li>
<li><b>Getting up, with the fix (Sokol): healthy.</b> The chart's line looks flat at about 65%, but practice gives a helping hand that is taken away as the boxer improves: Sokol's fell from 60% to 38% while his success held, so he is doing more of it himself. The exam (no help) will tell.</li>
<li><b>Boxing: slightly worse than the old Matt.</b> His old brain landed 0.65 punches a second; the new one 0.44, just under the 0.5 mark, and none to the head (the old one also struggled with the head). Its chart dips from 0.47 to 0.27 and was climbing back when the 45 minutes ran out.</li>
<li><b>Boxing, Sokol: steadier than Matt.</b> No dip: about 0.5 punches landed a second all lesson, ending at 0.53, and he fell over less as it went on (falls 0.22 &rarr; 0.04 an episode). He also started from 629 good get-up endings to practise from, against Matt's 99 from the broken lesson.
But his <b>boxing exam failed</b> on attack: 0.35 a second in the exam against 0.53 in practice. He lands well on Grandpa, Kim and Nick
(0.6-0.75) and almost never on Grandma, Matt, Trump or Zombie (0.04-0.13), and nothing to the head. And getting up, with no help in the exam: he stood up from only 2 of 60 knockdowns.</li>
<li><b>Kim: the best so far.</b> Walking exam passed every line; get-up ended at 64% with the help down to 14% (Sokol 38%), and saved 1,500 good endings.
In his boxing lesson he lands about 0.48 a second and, unlike Matt and Sokol, <b>12% of them to the head</b> (Kim also aimed at the head on the old body).
His <b>boxing exam failed</b> four lines, but it is the first real test: attack 0.49 a second (mark 0.5) but too soft (3.4 m/s, his mark 4.4),
10% to the head (49% on Nick). Against Grandma he was knocked down 60 times: he got up 32 times (53%, mark 80%), and only 2 of those 32 stayed
up 3 seconds once his boxing brain took over. <b>The hand-over from getting up to boxing is the weak link.</b></li>
<li><b>Zombie:</b> walking exam passed every line (four of four so far); get-up ended at 70% with 31% help, 1,500 good endings saved.
In his boxing lesson he lands fewer punches (0.30 a second, under the 0.5 mark) but <b>more than half go to the head</b> (52-63%), and he hardly falls (0.03-0.06 an episode).
His <b>boxing exam failed</b> three lines: attack 0.31 a second (50% to the head), getting up 9 of 60, and 3 of those 9 stayed up; footing, guard and chin pass.</li>
<li><b>Nick:</b> walking exam passed every line (five of five). Get-up ended at 57% with 18% help and 11% on his own, second only to Kim.
In his boxing lesson: about 0.40 a second, 25-38% to the head, falls 0.30 &rarr; 0.17 an episode.
His <b>boxing exam failed</b>, but with the best attack yet: <b>0.70 a second, 37% to the head</b>, only a touch too soft (3.8 m/s, mark 4.0); guard 79%.
He fell over against Matt and Sokol (1.3-1.6 a minute), got up 16 of 60, and none of the 16 stayed up once his boxing brain took over.</li>
<li><b>Trump:</b> walking exam passed every line (six of six). Get-up ended at 49% with 20% help and 0% on his own.
In his boxing lesson he lands few punches (about 0.22 a second) but over 60% to the head.
His <b>boxing exam failed</b> on attack (0.35 a second, but hard: 4.3 m/s, 59% to the head) and getting up (9 of 60, 2 stayed up); footing, guard and chin pass.</li>
<li><b>Grandpa: the weakest so far.</b> Walking exam passed every line (seven of seven). But his get-up lesson ended with the help still at 51% and 0% on his own,
and only 47 good get-up endings were saved (others 1,300-1,500). In his boxing lesson he barely lands (about 0.06 a second).
His <b>boxing exam failed</b>: attack 0.18 a second and soft (1.9 m/s, his mark 3.0), and he got up from 0 of 60 knockdowns (his gentler mark is 60%).
He is the best defender, though: guard 93%, no falls, no head shots taken.</li>
<li><b>Grandma:</b> walking exam passed every line, so <b>all eight walk on their new bodies</b>. Get-up ended at 50% with 42% help and 0% on her own (357 good endings saved).
In her boxing lesson: about 0.22 a second, 15-27% to the head.</li>
<li><b>Getting up: got worse while training.</b> Matt's success climbed to about 70% in the first 25 rounds, then fell back to about 50% and stayed there (the exam wants 80%). Cause found: a bug set its "try something new" noise to zero for the whole lesson (train_getup.py read the queue's
"--reset-std 0", meaning keep the noise, as "make it zero"), so it couldn't explore and its updates went wild (KL 1.3, normally under 0.05).
Fixed at 19:15, before Sokol's get-up lesson; the other seven get the fix. Matt's get-up and boxing lessons are queued again at the end (v3u2_matt, v3m2_matt).</li>
<li><b>Getting up is the real blocker (corrected 01:35).</b> The exam knocks each boxer down 60 times and gives no help. Matt stood up 0 times, Sokol 2,
Zombie 9, Kim 32 (mark 48). In practice the helping hand never reached zero in the 25-minute lesson: on its own (dashed lines) only Kim ever got up
(25%), and he is the one who got up most in the exam. An earlier version of this page said Matt and Sokol were barely knocked down; that was wrong.</li>
</ul>
<p class="dim">Charts are TensorBoard's numbers (logs/RUN.csv, smoothing 0.6, faint line = raw). Exams: logs/exam_v3f_NAME.json, logs/exam_v3m_NAME.log.</p>
</main></body></html>"""
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        f.write(html)
    print(f"{done}/8 done, ~{per.total_seconds()/3600:.1f} h a boxer, eta {eta:%Y-%m-%d %H:%M} -> {os.path.abspath(out)}")


if __name__ == "__main__":
    main()
