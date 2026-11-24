"""Before/after screenshot report for a UI change, in one command (AGENTS.md section 2.1, "UI documentation").

Captures PoBox's key screens from a LOCAL WebGL build (never the live site) at 1366x768 and 390x844, sets them
beside the "before" screens, and writes SCREENSHOTS/<yyyy-mm-dd>-<feature>/:
    index.html        before | after for every screen, with the notes on what changed
    side-by-side.png  every screen in one annotated image (changed regions boxed in red)
    before-*.png / after-*.png   the screens themselves (palette PNGs, a few hundred KB at most)

    uv run --no-sync --with playwright==1.49.0 --with pillow python scripts/screenshots.py --feature tab-overlay
        before = the WebGL release of HEAD~1 (or the newest webgl-* release before it), after = Builds/WebGL
    ... --before <git ref>             before = the newest webgl-* release built at or before that commit
    ... --before-dir <folder>          before = PNGs already taken, named *<screen>-<width>.png (e.g. home-1366.png)
    ... --before-build <folder>        before = another local WebGL build folder
    ... --after-build <folder>         after = a build other than Builds/WebGL
    ... --screens home=/ diag=/diag    the screens (default: home, diag, leaderboard)
    ... --note home="Loading bar ..."  a note per screen (repeatable)
    ... --wait 25                      seconds the game page gets to load before its screenshot

Rebuilding an old commit's WebGL player takes 20+ minutes, so "before" comes from the GitHub releases that
scripts/publish_webgl.ps1 keeps (the newest 3) or from PNGs you already have. Needs Chromium for Playwright
(`uv run --no-sync --with playwright==1.49.0 python -m playwright install chromium` once).
"""

from __future__ import annotations

import argparse
import datetime as dt
import functools
import html
import http.server
import json
import os
import shutil
import subprocess
import tempfile
import threading
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIZES = [(1366, 768), (390, 844)]
DEFAULT_SCREENS = {"home": "/", "diag": "/diag", "leaderboard": "/leaderboard.html"}


class _Handler(http.server.SimpleHTTPRequestHandler):
    """Serves a WebGL build the way staticwebapp.config.json does: gzip .unityweb, /diag -> diag.html."""

    def translate_path(self, path):
        p = path.split("?")[0]
        if p == "/diag":
            path = "/diag.html"
        return super().translate_path(path)

    def end_headers(self):
        if self.path.split("?")[0].endswith(".unityweb"):
            self.send_header("Content-Encoding", "gzip")
        super().end_headers()

    def guess_type(self, path):
        if str(path).endswith(".wasm"):
            return "application/wasm"
        if str(path).endswith(".unityweb"):
            return "application/octet-stream"
        return super().guess_type(path)

    def log_message(self, *args):
        pass


def _serve(folder):
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", 0), functools.partial(_Handler, directory=folder))
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv, f"http://127.0.0.1:{srv.server_address[1]}"


def _git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()


def _release_build(ref, work):
    """The newest webgl-* release whose commit is `ref` or an ancestor of it, unzipped into work/."""
    want = _git("rev-parse", ref)
    rels = json.loads(
        subprocess.run(
            ["gh", "release", "list", "--limit", "100", "--json", "tagName,createdAt"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout
    )
    for r in sorted((r for r in rels if r["tagName"].startswith("webgl-")), key=lambda r: r["createdAt"], reverse=True):
        sha = _git("rev-list", "-n", "1", r["tagName"])
        if subprocess.run(["git", "merge-base", "--is-ancestor", sha, want], cwd=ROOT).returncode == 0:
            subprocess.run(
                ["gh", "release", "download", r["tagName"], "-p", "webgl.zip", "-D", work, "--clobber"],
                cwd=ROOT,
                check=True,
            )
            out = os.path.join(work, "before")
            with zipfile.ZipFile(os.path.join(work, "webgl.zip")) as z:
                z.extractall(out)
            print(f"before: release {r['tagName']} (commit {sha[:7]})")
            return out
    raise SystemExit(f"no webgl-* release at or before {ref}: use --before-dir or --before-build")


def _shoot(folder, screens, prefix, out, wait):
    from playwright.sync_api import sync_playwright

    srv, base = _serve(folder)
    shots = {}
    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(args=["--use-angle=d3d11", "--ignore-gpu-blocklist"])
            for name, route in screens.items():
                for w, h in SIZES:
                    ctx = browser.new_context(viewport={"width": w, "height": h}, is_mobile=w < 500, has_touch=w < 500)
                    page = ctx.new_page()
                    page.goto(base + route, wait_until="load")
                    page.wait_for_timeout((wait if route == "/" else 3) * 1000)
                    path = os.path.join(out, f"{prefix}-{name}-{w}.png")
                    page.screenshot(path=path)
                    shots[(name, w)] = path
                    ctx.close()
            browser.close()
    finally:
        srv.shutdown()
    return shots


def _from_dir(folder, screens, out):
    shots = {}
    for name in screens:
        for w, _ in SIZES:
            hits = sorted(f for f in os.listdir(folder) if f.endswith(f"{name}-{w}.png"))
            if hits:
                dst = os.path.join(out, f"before-{name}-{w}.png")
                shutil.copyfile(os.path.join(folder, hits[0]), dst)
                shots[(name, w)] = dst
    return shots


def _shrink(path, width):
    """A palette PNG no wider than `width`: screenshots stay small in git."""
    from PIL import Image

    im = Image.open(path).convert("RGB")
    if im.width > width:
        im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    im.quantize(colors=128, method=Image.Quantize.MEDIANCUT).save(path, optimize=True)


def _changed_box(before, after):
    from PIL import Image, ImageChops

    a, b = Image.open(before).convert("RGB"), Image.open(after).convert("RGB")
    if a.size != b.size:
        return None
    return ImageChops.difference(a, b).convert("L").point(lambda v: 255 if v > 24 else 0).getbbox()


def _side_by_side(rows, path):
    from PIL import Image, ImageDraw

    pad, label, cell = 16, 28, 520

    def fit(path, box):
        im = Image.open(path).convert("RGB")
        k = min(1.0, cell / im.width)
        im = im.resize((round(im.width * k), round(im.height * k)), Image.LANCZOS)
        return im, (tuple(round(v * k) for v in box) if box else None)

    cells = []
    for b, a, title, box in rows:
        bi, _ = fit(b, None)
        ai, abox = fit(a, box)
        cells.append((bi, ai, title, abox))
    width = max(b.width + a.width for b, a, _, _ in cells) + 3 * pad
    height = sum(max(b.height, a.height) + label + pad for b, a, _, _ in cells) + pad
    sheet = Image.new("RGB", (width, height), (11, 15, 23))
    draw = ImageDraw.Draw(sheet)
    y = pad
    for b, a, title, box in cells:
        draw.text((pad, y + 6), f"{title}   BEFORE", fill=(154, 163, 181))
        draw.text((2 * pad + b.width, y + 6), "AFTER (changes boxed in red)", fill=(230, 233, 242))
        y += label
        sheet.paste(b, (pad, y))
        x = 2 * pad + b.width
        sheet.paste(a, (x, y))
        if box:
            draw.rectangle((x + box[0], y + box[1], x + box[2], y + box[3]), outline=(248, 113, 113), width=3)
        y += max(b.height, a.height) + pad
    sheet.quantize(colors=128, method=Image.Quantize.MEDIANCUT).save(path, optimize=True)


def _page(out, feature, before_label, after_label, rows, notes):
    parts = []
    for b, a, title, box, name in rows:
        note = html.escape(notes.get(name, "no change noted"))
        parts.append(
            f"<section><h2>{html.escape(title)}</h2><p class=note>{note}"
            f"{' · changed region boxed in the side-by-side image' if box else ' · no pixel change'}</p>"
            f"<div class=pair><figure><img src='{os.path.basename(b)}' alt='before: {html.escape(title)}'>"
            f"<figcaption>Before</figcaption></figure><figure><img src='{os.path.basename(a)}' "
            f"alt='after: {html.escape(title)}'><figcaption>After</figcaption></figure></div></section>"
        )
    doc = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>PoBox · {html.escape(feature)}</title>
<style>
:root{{--bg:#0b0f17;--surface:#121826;--line:#1f2737;--text:#e6e9f2;--muted:#9aa3b5}}
body{{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 Inter,system-ui,sans-serif}}
main{{max-width:1500px;margin:0 auto;padding:20px 16px}} h1{{font-size:22px}} h2{{font-size:16px;margin:28px 0 4px}}
.note,.meta{{color:var(--muted)}} .pair{{display:grid;grid-template-columns:1fr 1fr;gap:12px}}
figure{{margin:0;background:var(--surface);border:1px solid var(--line);border-radius:8px;padding:8px}}
img{{max-width:100%;height:auto;display:block}} figcaption{{color:var(--muted);font-size:12px;margin-top:4px}}
@media (max-width:700px){{.pair{{grid-template-columns:1fr}}}}
</style></head><body><main>
<h1>{html.escape(feature)}</h1>
<p class=meta>Before: {html.escape(before_label)} · After: {html.escape(after_label)} · taken {dt.date.today().isoformat()}
locally at 1366x768 and 390x844 by scripts/screenshots.py. All screens in one image:
<a href="side-by-side.png">side-by-side.png</a>.</p>
{"".join(parts)}
</main></body></html>
"""
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(doc)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--feature", required=True, help="short name, used in the folder name")
    src = ap.add_mutually_exclusive_group()
    src.add_argument("--before", default="HEAD~1", help="git ref; its WebGL comes from the webgl-* releases")
    src.add_argument("--before-dir", help="folder of PNGs named *<screen>-<width>.png")
    src.add_argument("--before-build", help="a local WebGL build folder")
    ap.add_argument("--after-build", default=os.path.join(ROOT, "Builds", "WebGL"))
    ap.add_argument("--screens", nargs="*", default=[], help="name=/route pairs")
    ap.add_argument("--note", action="append", default=[], help="screen=text (repeatable)")
    ap.add_argument("--wait", type=int, default=25, help="seconds the game page loads before its screenshot")
    ap.add_argument("--date", default=dt.date.today().isoformat())
    a = ap.parse_args()

    screens = dict(s.split("=", 1) for s in a.screens) or DEFAULT_SCREENS
    notes = dict(n.split("=", 1) for n in a.note)
    slug = "".join(c if c.isalnum() else "-" for c in a.feature.lower()).strip("-")
    out = os.path.join(ROOT, "SCREENSHOTS", f"{a.date}-{slug}")
    os.makedirs(out, exist_ok=True)
    if not os.path.isfile(os.path.join(a.after_build, "index.html")):
        raise SystemExit(f"no WebGL build at {a.after_build}: run scripts/publish_webgl.ps1 -NoPublish first")

    with tempfile.TemporaryDirectory() as work:
        if a.before_dir:
            before, before_label = (
                _from_dir(a.before_dir, screens, out),
                f"PNGs from {os.path.basename(os.path.normpath(a.before_dir))}",
            )
        else:
            build = a.before_build or _release_build(a.before, work)
            before, before_label = _shoot(build, screens, "before", out, a.wait), a.before_build or a.before
        after = _shoot(a.after_build, screens, "after", out, a.wait)
    after_label = f"{os.path.relpath(a.after_build, ROOT)} at {_git('rev-parse', '--short', 'HEAD')}"

    rows = []
    for (name, w), apath in after.items():
        bpath = before.get((name, w))
        if not bpath:
            print(f"no before for {name} at {w}: skipped")
            continue
        for p in (bpath, apath):
            _shrink(p, 900)
        rows.append((bpath, apath, f"{name} · {w} px", _changed_box(bpath, apath), name))
    if not rows:
        raise SystemExit("no screen has both a before and an after")
    _side_by_side([(b, a, t, box) for b, a, t, box, _ in rows], os.path.join(out, "side-by-side.png"))
    _page(out, a.feature, before_label, after_label, rows, notes)
    for f in sorted(os.listdir(out)):
        print(f"{os.path.relpath(os.path.join(out, f), ROOT)}  {os.path.getsize(os.path.join(out, f)) // 1024} KB")


if __name__ == "__main__":
    main()
