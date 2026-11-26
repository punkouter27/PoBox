"""Teacher-student distillation: a teacher that sees privileged state, a student that sees only what the game shows.

    python train_distill.py --stage v2 --xml models/v2/matt_bag.xml --run-name distill_matt \
        --teacher-resume checkpoints/onnx/matt_match.pt --teacher-iters 300 --student-iters 300 --domain-rand

Phase 1, the teacher (PPO): the env's own observation with envs/boxing_base.py privileged() on the end (the opponent's
true velocity; its own reserve, drive scale, daze and muscle activation; the domain randomisation's draw). A 103-number
checkpoint given as --teacher-resume is widened with zero weights on the new inputs (ppo.PPO.load(widen=True)), so the
teacher starts as that brain exactly. --teacher PATH skips the phase and loads a teacher saved by an earlier run.

Phase 2, the student (DAgger): the env's observation only, the exact 103 numbers MjBoxer.Observe builds (the stage's
own layout is not changed). The student starts from the teacher's weights on those inputs (or --student-resume), and
every iteration the worlds are driven by the student (the teacher's action instead in a share beta of the worlds, beta
falling from 1 to 0 over --beta-iters), the teacher labels every state visited with its action and its value, and the
student is fitted to them (action MSE, plus the value, which the game's win-probability bar reads) over the states of the
last --dagger-keep iterations. Its exploration noise (log_std) is the teacher's.

Exports latest.onnx (the student, as train_box.py exports: ppo.export_onnx, observation in, actions and value out),
latest.pt (the student, a PPO checkpoint train_box.py --resume takes), teacher_latest.pt and
latest_policy_config.json (the fighter's config with the rules it was trained under). One fighter (a bag, a solo or a
pair of the same boxer): a match of two different bodies is refused.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import os
import sys
import time

import torch
import torch.nn as nn

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
from envs import upgrades  # noqa: E402
from envs.boxing_base import PRIVILEGED_DIM  # noqa: E402
from ppo import PPO, PPOConfig, export_onnx  # noqa: E402


def make_env(args):
    common = dict(
        device=args.device,
        seed=args.seed,
        cuda_graph=args.device.startswith("cuda") and not args.no_cuda_graph,
        obs_noise=args.obs_noise,
        episode_len_s=args.episode_s,
        default_rules=True,
        **upgrades.kwargs(args),
    )
    if args.stage == "footwork":
        from envs.footwork import FootworkEnv

        return FootworkEnv(args.xml, args.num_envs, **common)
    from envs.match import MatchEnv

    return MatchEnv(args.xml, args.num_envs, push_vel=args.push_vel, **common)


def student_from_teacher(student: PPO, teacher: PPO, d: int) -> None:
    """The teacher's weights on the first d inputs, its normaliser's first d numbers and its exploration noise."""
    sd = {k: v.clone() for k, v in teacher.model.state_dict().items()}
    for net in ("actor", "critic"):
        sd[f"{net}.0.weight"] = sd[f"{net}.0.weight"][:, :d].clone()
    student.model.load_state_dict(sd)
    student.obs_rms.mean.copy_(teacher.obs_rms.mean[:d])
    student.obs_rms.var.copy_(teacher.obs_rms.var[:d])
    student.obs_rms.count = teacher.obs_rms.count


def policy_config(env, run_name: str, d: int, iters: int) -> dict:
    from tools.run_record import brain_rules

    manifest = dict(env.cfgs[0])
    manifest.update(
        {
            "action_scale": env.action_scale,
            "observation_size": d,
            **brain_rules(env, 0),
            "trained_by": {"run": run_name, "mode": f"distill_{env.mode}", "against": env.names, "iterations": iters},
        }
    )
    return manifest


def main(argv: list[str] | None = None) -> dict:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--xml", required=True)
    ap.add_argument("--stage", default="v2", choices=["v2", "footwork"], help="v2: envs/match.py; footwork")
    ap.add_argument("--num-envs", type=int, default=4096)
    ap.add_argument("--steps", type=int, default=24)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--no-cuda-graph", action="store_true")
    ap.add_argument("--episode-s", type=float, default=12.0)
    ap.add_argument("--obs-noise", type=float, default=1.0)
    ap.add_argument("--push-vel", type=float, default=0.4)
    ap.add_argument("--teacher", default="", help="a teacher saved by an earlier run (teacher_latest.pt): no phase 1")
    ap.add_argument("--teacher-resume", default="", help="phase 1 starts from this checkpoint (widened if 103 in)")
    ap.add_argument("--teacher-iters", type=int, default=200)
    ap.add_argument("--student-resume", default="", help="the student starts from this 103-number checkpoint")
    ap.add_argument("--student-iters", type=int, default=200)
    ap.add_argument(
        "--beta-iters", type=int, default=50, help="the teacher's share of the driving falls 1 -> 0 over these"
    )
    ap.add_argument("--dagger-keep", type=int, default=4, help="iterations of labelled states the student is fitted on")
    ap.add_argument("--student-lr", type=float, default=3e-4)
    ap.add_argument("--student-epochs", type=int, default=4)
    ap.add_argument("--student-batch", type=int, default=32768)
    ap.add_argument("--value-w", type=float, default=0.1, help="weight of the student's value fit")
    ap.add_argument("--lr", type=float, default=1e-3, help="the teacher's PPO learning rate")
    ap.add_argument("--init-std", type=float, default=0.5)
    ap.add_argument("--run-name", default="")
    ap.add_argument("--out-dir", default="", help="default checkpoints/<run-name>")
    upgrades.add_args(ap)
    args = ap.parse_args(argv)

    torch.manual_seed(args.seed)
    env = make_env(args)
    if env.hetero:
        raise SystemExit("train_distill.py takes one fighter (a bag, a solo or two of the same boxer)")
    N, K, A, D = env.N, env.K, env.A, env.obs_dim
    P = PRIVILEGED_DIM
    rows = N * K
    dev = args.device
    run_name = args.run_name or f"distill_{env.names[0]}_{time.strftime('%Y%m%d_%H%M%S')}"
    out = args.out_dir or os.path.join(HERE, "checkpoints", run_name)
    os.makedirs(out, exist_ok=True)
    csv_f = open(os.path.join(out, "distill.csv"), "w", newline="", encoding="utf-8")
    log = csv.writer(csv_f)
    log.writerow(["phase", "iter", "ep_return", "fall_rate", "kl_or_mse", "value_mse", "beta", "sps"])
    print(f"[distill] {env.names[0]} {env.mode}: obs {D} + privileged {P}, {rows} rows; out {out}", flush=True)

    minibatches = max(1, round(args.steps * rows / 24576))
    tcfg = PPOConfig(steps_per_env=args.steps, lr=args.lr, minibatches=minibatches, init_std=args.init_std)
    teacher = PPO(D + P, A, rows, dev, tcfg)
    if args.teacher:
        teacher.load(args.teacher)
    elif args.teacher_resume:
        teacher.load(args.teacher_resume, widen=True)

    def full(o: torch.Tensor) -> torch.Tensor:
        return torch.cat([o, env.privileged()], -1)

    # ---- phase 1: the teacher -----------------------------------------------------------------------
    obs = env.reset()
    t_iters = 0 if args.teacher else args.teacher_iters
    for it in range(t_iters):
        upgrades.tick(env, args, it)
        t0 = time.time()
        with torch.no_grad():
            for _ in range(args.steps):
                act = teacher.act(full(obs))
                obs, rew, done, timeout = env.step(act)
                teacher.record(rew, done, timeout)
        stats = teacher.update(full(obs))
        s = env.get_stats()
        if not math.isfinite(stats["kl"]) or not math.isfinite(s["ep_return"]):
            raise SystemExit(f"[abort] NaN in the teacher at iteration {it}")
        sps = args.steps * rows / max(1e-6, time.time() - t0)
        log.writerow(["teacher", it, s["ep_return"], s.get("fall_rate", 0.0), stats["kl"], "", "", int(sps)])
        if it % 10 == 0:
            print(f"teacher {it:5d} | {sps:7.0f} sps | ret {s['ep_return']:7.2f} | kl {stats['kl']:.4f}", flush=True)
    teacher.save(os.path.join(out, "teacher_latest.pt"), {"obs_dim": D + P, "privileged": P, "run_name": run_name})

    # ---- phase 2: the student (DAgger) ----------------------------------------------------------------
    student = PPO(D, A, rows, dev, PPOConfig(steps_per_env=args.steps, init_std=args.init_std))
    if args.student_resume:
        student.load(args.student_resume)
    else:
        student_from_teacher(student, teacher, D)
    opt = torch.optim.Adam(
        list(student.model.actor.parameters()) + list(student.model.critic.parameters()), args.student_lr
    )
    clip = student.cfg.obs_clip
    keep: list[tuple[torch.Tensor, torch.Tensor, torch.Tensor]] = []
    gen = torch.Generator(device=dev)
    gen.manual_seed(args.seed + 1)
    last = {}
    for it in range(args.student_iters):
        upgrades.tick(env, args, t_iters + it)
        t0 = time.time()
        beta = max(0.0, 1.0 - it / max(1, args.beta_iters))
        by_teacher = (torch.rand(rows, generator=gen, device=dev) < beta)[:, None]
        o_buf = torch.zeros(args.steps, rows, D, device=dev)
        a_buf = torch.zeros(args.steps, rows, A, device=dev)
        v_buf = torch.zeros(args.steps, rows, device=dev)
        with torch.no_grad():
            for t in range(args.steps):
                student.obs_rms.update(obs)
                xt = teacher.obs_rms.normalize(full(obs), teacher.cfg.obs_clip)
                mu_t = teacher.model.actor(xt)
                o_buf[t], a_buf[t], v_buf[t] = obs, mu_t, teacher.model.critic(xt).squeeze(-1)
                mu_s = student.model.actor(student.obs_rms.normalize(obs, clip))
                obs = env.step(torch.where(by_teacher, mu_t, mu_s))[0]
        keep.append((o_buf.reshape(-1, D), a_buf.reshape(-1, A), v_buf.reshape(-1)))
        keep = keep[-max(1, args.dagger_keep) :]
        O, Y, V = (torch.cat(x) for x in zip(*keep))
        n = O.shape[0]
        a_loss = v_loss = torch.zeros((), device=dev)
        for _ in range(args.student_epochs):
            perm = torch.randperm(n, generator=gen, device=dev)
            for i in range(0, n, args.student_batch):
                idx = perm[i : i + args.student_batch]
                x = student.obs_rms.normalize(O[idx], clip)
                a_loss = nn.functional.mse_loss(student.model.actor(x), Y[idx])
                v_loss = nn.functional.mse_loss(student.model.critic(x).squeeze(-1), V[idx])
                opt.zero_grad()
                (a_loss + args.value_w * v_loss).backward()
                nn.utils.clip_grad_norm_(student.model.parameters(), 1.0)
                opt.step()
        s = env.get_stats()
        sps = args.steps * rows / max(1e-6, time.time() - t0)
        last = {
            "action_mse": float(a_loss.detach()),
            "value_mse": float(v_loss.detach()),
            "ep_return": s["ep_return"],
            "beta": beta,
        }
        if not all(math.isfinite(v) for v in last.values()):
            raise SystemExit(f"[abort] NaN in the student at iteration {it}")
        log.writerow(
            [
                "student",
                it,
                s["ep_return"],
                s.get("fall_rate", 0.0),
                last["action_mse"],
                last["value_mse"],
                beta,
                int(sps),
            ]
        )
        if it % 10 == 0:
            print(
                f"student {it:5d} | {sps:7.0f} sps | ret {s['ep_return']:7.2f} | action mse {last['action_mse']:.5f} "
                f"value mse {last['value_mse']:.4f} | beta {beta:.2f}",
                flush=True,
            )
    csv_f.close()

    iters = t_iters + args.student_iters
    extra = {
        "iter": iters,
        "mode": env.mode,
        "fighters": env.names,
        "fighter": env.names[0],
        "obs_dim": D,
        "act_dim": A,
    }
    student.save(os.path.join(out, "latest.pt"), {**extra, "action_scale": env.action_scale, "run_name": run_name})
    export_onnx(student, os.path.join(out, "latest.onnx"), D, fixed_batch=env.mode == "footwork")
    with open(os.path.join(out, "latest_policy_config.json"), "w", encoding="utf-8") as fh:
        json.dump(policy_config(env, run_name, D, iters), fh, indent=2)
    print(f"[distill] student exported to {os.path.join(out, 'latest.onnx')} ({D} in, {A} out)", flush=True)
    return {"out": out, "obs_dim": D, "privileged": P, **last}


if __name__ == "__main__":
    main()
