"""Archive promoted and milestone training checkpoints off-tree (Item 16: Keep checkpoints).

Checkpoints live outside the repo under ``../PoBox_checkpoints_archive`` (or ``POBOX_CKPT_ARCHIVE``)
so training cleanup and git never lose the ``.pt`` weights + critic + optimizer + ``run_config.json``
needed to warm-start future lessons or audit promoted brains.

Examples:
    python tools/archive_checkpoints.py --from-onnx --tag baseline-2026-11-13
    python tools/archive_checkpoints.py --run v3f_grandma --exam logs/exam_v3f_grandma.json
    python tools/archive_checkpoints.py --promote Grandma footwork --run v3f_grandma
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import time
from pathlib import Path
from typing import Iterable, Optional

TRAINING_DIR = Path(__file__).resolve().parent.parent
REPO_DIR = TRAINING_DIR.parent
DEFAULT_ARCHIVE = Path(os.environ.get("POBOX_CKPT_ARCHIVE", str(REPO_DIR.parent / "PoBox_checkpoints_archive")))


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _copy_entry(src: Path, dst_dir: Path, records: list) -> None:
    dst_dir.mkdir(parents=True, exist_ok=True)
    dst = dst_dir / src.name
    shutil.copy2(src, dst)
    records.append({"file": src.name, "size": src.stat().st_size, "sha256": _sha256(dst)})


def _latest_pt(run_dir: Path) -> Optional[Path]:
    latest = run_dir / "latest.pt"
    if latest.is_file():
        return latest
    pts = sorted(run_dir.glob("*.pt"), key=lambda p: p.stat().st_mtime)
    return pts[-1] if pts else None


def _load_index(dest: Path) -> list:
    idx = dest / "index.json"
    if idx.is_file():
        return json.loads(idx.read_text(encoding="utf-8"))
    return []


def _save_index(dest: Path, entries: list) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    (dest / "index.json").write_text(json.dumps(entries, indent=2) + "\n", encoding="utf-8")
    lines = ["# PoBox Checkpoints Archive", "", "| When | Tag | Kind | Files | Target |", "|---|---|---|---|---|"]
    for e in entries[-100:]:
        files = ", ".join(f["file"] for f in e.get("files", []))
        lines.append(f"| {e['written']} | {e['tag']} | {e['kind']} | {files} | `{e['folder']}` |")
    (dest / "INDEX.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def archive_from_onnx(dest: Path, tag: str) -> Path:
    stamp = time.strftime("%Y-%m-%d_%H%M%S")
    folder_name = f"shipped_{stamp}_{tag}" if tag else f"shipped_{stamp}"
    out = dest / folder_name
    records = []
    onnx_pts = sorted((TRAINING_DIR / "checkpoints" / "onnx").glob("*.pt"))
    for p in onnx_pts:
        _copy_entry(p, out / "onnx_pt", records)
    boxers_dir = REPO_DIR / "Assets" / "Boxers"
    for bdir in sorted(p for p in boxers_dir.iterdir() if p.is_dir()):
        for fn in ("config.json", "footwork.onnx", "getup.onnx", "match.onnx", "policy.onnx"):
            fp = bdir / fn
            if fp.is_file():
                _copy_entry(fp, out / "Assets_Boxers" / bdir.name, records)
    meta = {"written": time.strftime("%Y-%m-%d %H:%M:%S"), "tag": tag or "shipped", "kind": "shipped_onnx_snapshot",
            "folder": folder_name, "files": records}
    (out / "manifest.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    idx = _load_index(dest)
    idx.append(meta)
    _save_index(dest, idx)
    return out


def archive_run(dest: Path, run: str, tag: str, ckpt: Optional[str] = None,
                extra_files: Iterable[Path] = (), promote: Optional[tuple] = None) -> Path:
    run_dir = Path(run)
    if not run_dir.is_dir():
        run_dir = TRAINING_DIR / "checkpoints" / run
    if not run_dir.is_dir():
        raise FileNotFoundError(f"Run directory not found: {run}")
    pt = Path(ckpt) if ckpt else _latest_pt(run_dir)
    if pt is None or not pt.is_file():
        raise FileNotFoundError(f"No .pt checkpoint found in {run_dir}")

    stamp = time.strftime("%Y-%m-%d_%H%M%S")
    label = tag or run_dir.name
    prefix = f"promoted_{promote[0].lower()}_{promote[1]}" if promote else "run"
    folder_name = f"{prefix}_{stamp}_{label}"
    out = dest / folder_name
    records = []
    _copy_entry(pt, out, records)
    for meta_name in ("run_config.json", "exam.json"):
        mp = run_dir / meta_name
        if mp.is_file():
            _copy_entry(mp, out, records)
    for p in extra_files:
        if p.is_file():
            _copy_entry(p, out, records)
    if promote:
        boxer, brain = promote
        onnx_path = REPO_DIR / "Assets" / "Boxers" / boxer / f"{brain}.onnx"
        cfg_path = REPO_DIR / "Assets" / "Boxers" / boxer / "config.json"
        for p in (onnx_path, cfg_path):
            if p.is_file():
                _copy_entry(p, out / "promoted_assets", records)

    meta = {"written": time.strftime("%Y-%m-%d %H:%M:%S"), "tag": label,
            "kind": "promoted" if promote else "run", "run": run_dir.name,
            "folder": folder_name, "files": records}
    (out / "manifest.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    idx = _load_index(dest)
    idx.append(meta)
    _save_index(dest, idx)
    return out


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--dest", type=Path, default=DEFAULT_ARCHIVE)
    ap.add_argument("--tag", default="")
    ap.add_argument("--from-onnx", action="store_true", help="Snapshot checkpoints/onnx/*.pt + Assets/Boxers/*")
    ap.add_argument("--run", default="", help="Run name under checkpoints/ or full path")
    ap.add_argument("--ckpt", default="", help="Specific .pt path (default: latest in --run)")
    ap.add_argument("--exam", nargs="*", default=[], help="Extra exam/log JSON files to archive with --run")
    ap.add_argument("--promote", nargs=2, metavar=("BOXER", "BRAIN"), help="Also archive Assets/Boxers/BOXER/BRAIN.onnx")
    args = ap.parse_args()

    if args.from_onnx:
        out = archive_from_onnx(args.dest, args.tag)
        print(f"archived onnx snapshot -> {out}")
    if args.run:
        out = archive_run(args.dest, args.run, args.tag, args.ckpt or None,
                          [Path(x) for x in args.exam], tuple(args.promote) if args.promote else None)
        print(f"archived run -> {out}")
    if not args.from_onnx and not args.run:
        ap.error("Specify --from-onnx and/or --run")


if __name__ == "__main__":
    main()
