"""A new boxer's warm start from another boxer's shipped v3 brain (2026-11-16, Timmy from Sokol).

    python tools/warm_from_boxer.py --ckpt checkpoints/onnx/match_Sokol.pt --source sokol --target timmy \
        --out checkpoints/v3/match_timmy.pt

A checkpoint rebuilt from ONNX (tools/onnx_to_pt.py) does not say what action scale its brain was trained at, so
tools/rescale_actions.py would take it for 0.5 (every brain before v3). A brain shipped on the v3 body asks for its
joint targets at its own boxer's per-joint scale (models/v3/<source>_policy_config.json action_scale_j); that is written
into a copy of the checkpoint first, then rescale_actions.py turns it onto the target's scale joint by joint, with its
--check (targets unchanged wherever the old action lay inside its clip).
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile

import torch

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ckpt", required=True, help="a checkpoint from tools/onnx_to_pt.py")
    ap.add_argument("--source", required=True, help="the boxer the brain was trained on (its v3 config gives the scale)")
    ap.add_argument("--target", required=True, help="the boxer it is for")
    ap.add_argument("--models", default="models/v3")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    path = lambda p: p if os.path.isabs(p) else os.path.join(HERE, p)

    src_cfg = json.load(open(path(os.path.join(args.models, f"{args.source}_policy_config.json")), encoding="utf-8"))
    ck = torch.load(path(args.ckpt), map_location="cpu", weights_only=False)
    ck.setdefault("extra", {})["action_scale_j"] = list(src_cfg["action_scale_j"])
    ck["extra"]["warm_from"] = {"ckpt": args.ckpt, "boxer": args.source}
    with tempfile.TemporaryDirectory() as tmp:
        staged = os.path.join(tmp, "scaled.pt")
        torch.save(ck, staged)
        os.makedirs(os.path.dirname(path(args.out)), exist_ok=True)
        r = subprocess.run([sys.executable, os.path.join(HERE, "tools", "rescale_actions.py"), "--ckpt", staged,
                            "--config", os.path.join(args.models, f"{args.target}_policy_config.json"),
                            "--out", args.out, "--check"], cwd=HERE)
    sys.exit(r.returncode)


if __name__ == "__main__":
    main()
