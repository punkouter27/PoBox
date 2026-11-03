"""The overnight report (2026-11-02): one HTML page, charts drawn as inline SVG straight from the runs' CSVs (no
matplotlib), the exam lines before and after, and each main chart told three ways (toddler, child, adult).

    python tools/night_report.py --out ../DOCS/reports/2026-11-03-overnight.html
"""
import argparse, csv, html, json, os

LOGS = "logs"
COLOURS = ["#2a6fdb", "#d9822b", "#1f9e6e", "#b03a8c", "#7a6cd8"]

# Main charts: (title, [(label, [runs joined end to end], column)], y label, mark, explanations)
MAIN = [
    ("Nick: staying up after getting up",
     [("Nick", ["v3m8_nick", "v3m9_nick"], "carry_on")], "share still up 3 s after the hand-over", 0.9,
     ("Nick falls down, gets up, and then has to stay up. The line going up means he falls over less.",
      "When Nick gets back up, the boxing brain takes over. Before, it often tripped in the first seconds. Each "
      "point is a practice round: higher means more get-ups where he was still standing three seconds later.",
      "carry_on in the gauntlet env: share of bank-start episodes (get-up end states, any facing) still up at "
      "recover_s = 3 s. v3m8 (recover_w 1.0) then v3m9 (recover_w 2.0). The dashed line is the exam mark (90%).")),
    ("Trump: punches that land",
     [("Trump", ["v3m5_trump", "v3m6_trump"], "hits_per_s")], "punches landed a second", 0.5,
     ("Trump is learning to hit. The line going up means more punches land.",
      "Trump stood and blocked well but hardly ever landed a punch. He practised against the other seven; the "
      "line is how many of his punches land each second.",
      "hits_per_s across all opponents of the gauntlet, v3m5 then v3m6 (warm start, same reward, any-facing "
      "get-up starts). The dashed line is the exam mark (0.5/s, measured in C MuJoCo, not in training).")),
    ("Getting up without help: Matt, Grandma, Grandpa",
     [(n.title(), [f"v3u4_{n}", f"v3u5_{n}"], f"{n}_up_rate_unaided") for n in ("matt", "grandma", "grandpa")],
     "share of falls they get up from on their own", None,
     ("They fall down. Can they get up by themselves? Up is good.",
      "In practice a helping hand lifts a little at first and slowly lets go. These lines count only the times "
      "nobody helped: how often each boxer got up all alone.",
      "up_rate_unaided of the get-up env (worlds with assist 0, unaided_share 0.3). v3u4 (fade_above 0.45) "
      "then v3u5 (fade_above 0.6: the hand lets go only while the aided rate stays over 60%).")),
]
SMALL = ("ep_return", "value_loss")


def read(run):
    p = os.path.join(LOGS, f"{run}.csv")
    if not os.path.exists(p):
        return []
    with open(p, encoding="utf-8") as f:
        return list(csv.DictReader(f))


def series(runs, col):
    """The runs end to end on one hours axis; returns points and where each run starts."""
    pts, starts, t0 = [], [], 0.0
    for run in runs:
        rows = [r for r in read(run) if r.get(col) not in (None, "")]
        if not rows:
            continue
        starts.append((t0, run))
        for r in rows:
            pts.append((t0 + float(r["hours"]), float(r[col])))
        t0 = pts[-1][0]
    return pts, starts


def smooth(pts, k=15):
    out = []
    for i in range(len(pts)):
        w = pts[max(0, i - k + 1):i + 1]
        out.append((pts[i][0], sum(v for _, v in w) / len(w)))
    return out


def svg(lines, ylabel, mark=None, w=640, h=260):
    """lines: [(label, pts, starts)]."""
    allp = [p for _, pts, _ in lines for p in pts]
    if not allp:
        return "<p><i>no data yet</i></p>"
    x1 = max(p[0] for p in allp) or 1.0
    ys = [p[1] for p in allp] + ([mark] if mark is not None else [])
    y0, y1 = min(0.0, min(ys)), max(ys) * 1.08 or 1.0
    L, R, T, B = 56, 12, 12, 36
    X = lambda x: L + (w - L - R) * x / x1
    Y = lambda y: T + (h - T - B) * (1 - (y - y0) / (y1 - y0))
    s = [f'<svg viewBox="0 0 {w} {h}" width="100%" style="max-width:{w}px" role="img" aria-label="{html.escape(ylabel)}">',
         f'<rect x="{L}" y="{T}" width="{w-L-R}" height="{h-T-B}" fill="#fafafa" stroke="#ddd"/>']
    for i in range(5):
        v = y0 + (y1 - y0) * i / 4
        s.append(f'<text x="{L-6}" y="{Y(v)+4:.1f}" font-size="11" text-anchor="end" fill="#555">{v:.2f}</text>'
                 f'<line x1="{L}" x2="{w-R}" y1="{Y(v):.1f}" y2="{Y(v):.1f}" stroke="#eee"/>')
    for i in range(5):
        v = x1 * i / 4
        s.append(f'<text x="{X(v):.1f}" y="{h-B+16}" font-size="11" text-anchor="middle" fill="#555">{v:.1f} h</text>')
    s.append(f'<text x="{L}" y="{h-4}" font-size="11" fill="#555">{html.escape(ylabel)}</text>')
    if mark is not None:
        s.append(f'<line x1="{L}" x2="{w-R}" y1="{Y(mark):.1f}" y2="{Y(mark):.1f}" stroke="#c0392b" stroke-dasharray="6 4"/>'
                 f'<text x="{w-R-4}" y="{Y(mark)-4:.1f}" font-size="11" text-anchor="end" fill="#c0392b">mark {mark}</text>')
    for i, (label, pts, starts) in enumerate(lines):
        c = COLOURS[i % len(COLOURS)]
        for t, run in starts[1:]:
            s.append(f'<line x1="{X(t):.1f}" x2="{X(t):.1f}" y1="{T}" y2="{h-B}" stroke="#bbb" stroke-dasharray="2 3"/>'
                     f'<text x="{X(t)+3:.1f}" y="{T+12}" font-size="10" fill="#888">{html.escape(run)}</text>')
        raw = " ".join(f"{X(x):.1f},{Y(y):.1f}" for x, y in pts)
        sm = " ".join(f"{X(x):.1f},{Y(y):.1f}" for x, y in smooth(pts))
        s.append(f'<polyline points="{raw}" fill="none" stroke="{c}" stroke-opacity="0.18"/>'
                 f'<polyline points="{sm}" fill="none" stroke="{c}" stroke-width="2.2"/>')
        s.append(f'<text x="{L+8}" y="{T+16+14*i}" font-size="12" fill="{c}" font-weight="600">{html.escape(label)}</text>')
    s.append("</svg>")
    return "".join(s)


def exam_rows(pairs):
    """pairs: [(boxer, before exam json, after exam json)] -> table rows of every line found in either."""
    out = []
    for name, before, after in pairs:
        b = load_lines(before, name)
        a = load_lines(after, name)
        for line in list(dict.fromkeys(list(b) + list(a))):
            out.append((name.title(), line, b.get(line), a.get(line)))
    return out


def load_lines(run, name):
    p = os.path.join(LOGS, f"{run}.json")
    if not run or not os.path.exists(p):
        return {}
    with open(p, encoding="utf-8") as f:
        return {k: v for k, v in json.load(f)["boxers"][name]["lines"].items()}


def cell(v):
    if v is None:
        return "<td class=na>not run</td>"
    return f'<td class={"ok" if v["ok"] else "no"}>{"PASS" if v["ok"] else "FAIL"}<br><small>{html.escape(v["text"])}</small></td>'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--title", default="PoBox overnight training, 2-3 November 2026")
    ap.add_argument("--summary", default="", help="an HTML paragraph written after the runs: what happened, in plain words")
    ap.add_argument("--exams", default="", help="JSON list of [boxer, before exam run, after exam run]")
    a = ap.parse_args()

    body = [f"<h1>{html.escape(a.title)}</h1>", a.summary]
    for title, groups, ylabel, mark, (tod, kid, adult) in MAIN:
        lines = [(label, *series(runs, col)) for label, runs, col in groups]
        body.append(f"<section><h2>{html.escape(title)}</h2>{svg(lines, ylabel, mark)}"
                    f"<p class=lvl><b>Toddler:</b> {html.escape(tod)}</p><p class=lvl><b>Child:</b> {html.escape(kid)}</p>"
                    f"<p class=lvl><b>Adult:</b> {html.escape(adult)}</p>"
                    "<p class=note>Faint line: every training round; solid line: the average of the last 15.</p></section>")
    if a.exams:
        rows = exam_rows(json.loads(a.exams))
        t = ["<table><tr><th>boxer</th><th>exam line</th><th>before</th><th>after</th></tr>"]
        t += [f"<tr><td>{n}</td><td>{html.escape(l)}</td>{cell(b)}{cell(af)}</tr>" for n, l, b, af in rows]
        body.append("<section><h2>The exam, before and after</h2><p>Measured in plain C MuJoCo 3.5.0 (the version the "
                    "game runs), 60 trials a line; getting up under the game's rule.</p>" + "".join(t) + "</table></section>")
    small = []
    for run in ("v3m9_nick", "v3m6_trump", "v3u5_matt", "v3u5_grandma", "v3u5_grandpa"):
        for col in SMALL:
            pts, starts = series([run], col)
            small.append(f"<div class=small><h3>{run}: {col}</h3>{svg([(run, pts, starts)], col, None, 420, 200)}</div>")
    body.append("<section><h2>Reward and loss, run by run</h2><p>ep_return is the reward an episode earns (up is "
                "better); value_loss is how wrong the critic's guesses are (it settles as learning steadies).</p>"
                + "".join(small) + "</section>")
    css = ("body{font-family:system-ui,sans-serif;max-width:900px;margin:24px auto;padding:0 16px;color:#222}"
           "h2{margin-top:36px}.lvl{margin:4px 0}.note{color:#777;font-size:13px}"
           "table{border-collapse:collapse;width:100%;font-size:13px}td,th{border:1px solid #ddd;padding:6px;vertical-align:top}"
           ".ok{background:#e7f6ec}.no{background:#fbeaea}.na{color:#999}.small{display:inline-block;width:49%;vertical-align:top}"
           ".small h3{font-size:13px;margin:12px 0 2px}")
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        f.write(f"<!DOCTYPE html><html><head><meta charset=utf-8><title>{html.escape(a.title)}</title>"
                f"<style>{css}</style></head><body>{''.join(body)}</body></html>")
    print("wrote", a.out)


if __name__ == "__main__":
    main()
