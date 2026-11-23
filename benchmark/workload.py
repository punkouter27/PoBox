"""PoBox Workload: the real training/train_gauntlet.py (own PPO, MuJoCo Warp MatchEnv), the v3 match recipe.

Run by serve.py with the repo's training venv:  .venv/Scripts/python.exe benchmark/workload.py
The recipe is `v3m_matt` in training/queues/queue_v3_staged.json (the committed v3 match gauntlet: Matt learns
against seven frozen boxers in 4608 rings), with two things a fresh clone cannot have replaced:
- warm starts: the shipped Assets/_Project/RL_Models/NAME/match.onnx brains, rebuilt by tools/onnx_to_pt.py into
  training/checkpoints/_benchmark_warm/ (the recipe resumes from checkpoints/v3/*, made the same way on the old PC);
- no --handover bank (logs/handover_v3_matt.npz is a run artifact, not in git): every episode starts in the ring.
The loop has no timing hooks of its own, so PPO is wrapped: Sim time runs from the learner's first act() of an
iteration to its update(), Learn time is update() (both CUDA-synchronised); one iteration = --steps Steps.
Everything the run writes is under the run name `_benchmark` (training/checkpoints/, training/logs/; gitignored).
"""

from __future__ import annotations

import dataclasses
import hashlib
import os
import runpy
import shutil
import sys
import time
from importlib import metadata
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
TRAINING = ROOT / "training"
RUN = "_benchmark"
WARM = TRAINING / "checkpoints" / "_benchmark_warm"
LEARNER = "matt"
FOES = ["sokol", "kim", "zombie", "nick", "trump", "grandpa", "grandma"]
# v3m_matt's recipe (queue_v3_staged.json) after --resume/--against. Change these and the Workload ID changes.
RECIPE = (
    "--stage match --models models/v3 --daze --obs-state --hit-push 0.6 --stagger-v 5 --hurt-full 400 "
    "--body-drain 0.0025 --hit-w 1.5 --taken-w 1.0 --block-w 0.5 --fall-penalty 12 --selfcontact-w 0.02 "
    "--repeat-decay 0.7 --combo-w 0.1 --hurt-start 0.2 --clinch-w 0.1 --fatigue-ceiling --sep-max 3.5 "
    "--num-envs 4608 --more-iters 600 --max-hours 0.75 --max-std 0.35 "
    "--done-when hits_per_s>=1.2,falls<=0.03,repeat_share<=0.6 --done-window 40"
).split()
# Benchmark-only: never save a checkpoint or ONNX (nothing is kept from the run).
QUIET = ["--save-every", "100000", "--no-onnx"]
RUNNING: dict = {"meter": None}  # the Meter once the learner exists, so a later failure lands in its row


def version(pkg: str) -> str | None:
    try:
        return metadata.version(pkg)
    except metadata.PackageNotFoundError:
        return None


def sha(path: Path) -> str:
    return hashlib.sha1(path.read_bytes()).hexdigest()[:10]


def warm_start(name: str) -> str:
    """Assets/_Project/RL_Models/NAME/match.onnx as a checkpoint the gauntlet can resume from or freeze (cached)."""
    src = ROOT / "Assets" / "_Project" / "RL_Models" / name.capitalize() / "match.onnx"
    dst = WARM / f"match_{name}.pt"
    if not dst.exists() or dst.stat().st_mtime < src.stat().st_mtime:
        from tools.onnx_to_pt import convert

        WARM.mkdir(parents=True, exist_ok=True)
        convert(str(src), str(dst), 0.3, 1e9)
    return dst.relative_to(TRAINING).as_posix()


def main() -> None:
    import torch
    from envs.match import MatchEnv
    from ppo import PPO

    xmls: list[str] = []
    state = {"t_sim": None}
    sync = torch.cuda.synchronize if torch.cuda.is_available() else (lambda: None)

    env_init = MatchEnv.__init__

    def env_init_(self, xml, *a, **k):
        xmls.append(xml)
        env_init(self, xml, *a, **k)

    ppo_init, ppo_act, ppo_update = PPO.__init__, PPO.act, PPO.update

    def ppo_init_(self, obs_dim, act_dim, num_envs, device, cfg):
        ppo_init(self, obs_dim, act_dim, num_envs, device, cfg)
        self._bench_learner = num_envs > 1  # the frozen opponents are built with one env each
        if not self._bench_learner:
            return
        net = [m.out_features for m in self.model.actor if isinstance(m, torch.nn.Linear)][:-1]
        workload = dict(
            repo="PoBox",
            trainer=f"PoBox ppo.py + mujoco_warp {version('mujoco-warp')}",
            script="training/train_gauntlet.py",
            args=" ".join(sys.argv[1:]),
            num_envs=num_envs,
            horizon=cfg.steps_per_env,
            net=net,
            config_hash=core.workload_id(
                {
                    "ppo": dataclasses.asdict(cfg),
                    "argv": sys.argv[1:],
                    "xml": {os.path.basename(x): sha(Path(x)) for x in xmls},
                    "reward": sha(TRAINING / "config" / "reward.json"),
                    "brains": {
                        n: sha(ROOT / "Assets" / "_Project" / "RL_Models" / n.capitalize() / "match.onnx")
                        for n in [LEARNER, *FOES]
                    },
                }
            ),
            note="warm starts from the shipped Assets/_Project/RL_Models/*/match.onnx; no hand-over bank (not in a fresh clone)",
        )
        versions = dict(
            python=sys.version.split()[0],
            torch=torch.__version__,
            mujoco=version("mujoco"),
            mujoco_warp=version("mujoco-warp"),
            warp=version("warp-lang"),
        )
        RUNNING["meter"] = core.Meter(workload, versions)

    def ppo_act_(self, raw_obs):
        if getattr(self, "_bench_learner", False) and state["t_sim"] is None:
            state["t_sim"] = time.perf_counter()
        return ppo_act(self, raw_obs)

    def ppo_update_(self, last_raw_obs):
        if not getattr(self, "_bench_learner", False):
            return ppo_update(self, last_raw_obs)
        sync()
        t1 = time.perf_counter()
        out = ppo_update(self, last_raw_obs)
        sync()
        t2 = time.perf_counter()
        sim = t1 - (state["t_sim"] or t1)
        state["t_sim"] = None
        RUNNING["meter"].block(sim, t2 - t1, self.cfg.steps_per_env)
        return out

    MatchEnv.__init__, PPO.__init__, PPO.act, PPO.update = env_init_, ppo_init_, ppo_act_, ppo_update_

    # A throwaway run: only paths named after RUN are ever removed, never another run's.
    for p in (TRAINING / "checkpoints" / RUN, TRAINING / "logs" / "tb" / RUN):
        shutil.rmtree(p, ignore_errors=True)
    for suffix in (".csv", ".args.json", ".status.json", ".stop"):
        (TRAINING / "logs" / f"{RUN}{suffix}").unlink(missing_ok=True)

    os.chdir(TRAINING)
    against = [f"{n}={warm_start(n)}" for n in FOES]
    args = ["--run-name", RUN, "--name", LEARNER, "--resume", warm_start(LEARNER), "--against", *against]
    sys.argv = ["train_gauntlet.py", *args, *RECIPE, *QUIET]
    runpy.run_path(str(TRAINING / "train_gauntlet.py"), run_name="__main__")
    raise RuntimeError("the training loop ended before the measured window was complete")


if __name__ == "__main__":
    sys.path.insert(0, str(TRAINING))
    try:
        main()
    except BaseException as e:  # noqa: BLE001 - every failure becomes a failed row, never a silent miss
        meter = RUNNING["meter"] or core.Meter(
            {"repo": "PoBox", "script": "training/train_gauntlet.py"}, {"python": sys.version.split()[0]}
        )
        meter.finish(ok=False, error=core.fail_text(e))
