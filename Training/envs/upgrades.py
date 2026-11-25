"""The command-line side of the opt-in trainer upgrades of 2026-11-25, shared by train_box.py and train_distill.py.

    --domain-rand (+ --dr-*)   envs/randomize.py, ranges in config/domain_rand.json
    --muscle hill (+ --muscle-*) envs/muscle.py; the game runs it from the boxer config's "muscle" block
    --energy-w W               the mechanical work sum |tau omega| charged at W (reward config's mech_work)
    --hit-bank PATH (+ --hit-*) envs/hit_bank.py, a bank written by tools/hits_to_bank.py

Every one is off by default: with none given, kwargs() is {} and the envs are exactly as before.
"""

from __future__ import annotations

import argparse


def add_args(ap: argparse.ArgumentParser) -> None:
    g = ap.add_argument_group("upgrades (2026-11-25, all off by default)")
    g.add_argument(
        "--domain-rand",
        action="store_true",
        help="per-episode friction, body mass, PD kp/kd, actuator delay, timestep jitter and sensor noise "
        "(envs/randomize.py; ranges in config/domain_rand.json, the --dr-* knobs over those)",
    )
    for name, kind, what in (
        ("friction", float, "+- share of contact friction"),
        ("mass", float, "+- share of each body's mass"),
        ("kp", float, "+- share of the drives' stiffness"),
        ("kd", float, "+- share of the drives' damping"),
        ("delay-max", int, "most control steps the drive targets lag"),
        ("dt-jitter", float, "+- share of the physics timestep"),
        ("obs-noise", float, "extra sensor noise, on top of --obs-noise"),
    ):
        g.add_argument(f"--dr-{name}", type=kind, default=None, help=f"--domain-rand: {what}")
    g.add_argument(
        "--muscle",
        default="",
        choices=["", "hill"],
        help="hill: a Hill-type muscle on each drive (force-length-velocity, ~30 ms activation; envs/muscle.py) in "
        "place of the linear speed limit; written into the run's policy config, which the game's MjBoxer reads",
    )
    g.add_argument("--muscle-for", nargs="*", default=[], help="--muscle: only these fighters (default all)")
    g.add_argument("--muscle-peak", type=float, default=None, help="--muscle: peak force, share of force_limit")
    g.add_argument("--muscle-tau", type=float, default=None, help="--muscle: activation time constant, s (0.03)")
    g.add_argument("--muscle-tau-scale", type=float, default=None, help="--muscle: times the tau (an old body: > 1)")
    g.add_argument(
        "--energy-w",
        type=float,
        default=None,
        help="weight of the mechanical work sum |tau omega| (W), its own term rt_work; default: reward config's mech_work (0)",
    )
    g.add_argument("--hit-bank", default="", help="a tools/hits_to_bank.py .npz: shoves drawn from real hits")
    g.add_argument("--hit-every-s", type=float, default=4.0, help="--hit-bank: about one hit a fighter this often")
    g.add_argument("--hit-scale", type=float, default=1.0, help="--hit-bank: times every impulse")
    g.add_argument(
        "--hit-ramp-iters",
        type=int,
        default=0,
        help="--hit-bank: impulses ramp from 0 to full over this many iterations",
    )


def kwargs(args) -> dict:
    """The env keyword arguments for what was asked; {} when nothing was."""
    out = {}
    if args.domain_rand:
        from .randomize import ranges

        out["domain_rand"] = ranges(
            friction=args.dr_friction,
            mass=args.dr_mass,
            kp=args.dr_kp,
            kd=args.dr_kd,
            delay_max=args.dr_delay_max,
            dt_jitter=args.dr_dt_jitter,
            obs_noise=args.dr_obs_noise,
        )
    if args.muscle:
        out["muscle"] = args.muscle
        out["muscle_for"] = tuple(args.muscle_for)
        out["muscle_over"] = {"peak": args.muscle_peak, "tau_s": args.muscle_tau, "tau_scale": args.muscle_tau_scale}
    if args.energy_w is not None:
        out["energy_w"] = args.energy_w
    if args.hit_bank:
        out.update(hit_bank=args.hit_bank, hit_every_s=args.hit_every_s, hit_scale=args.hit_scale)
    return out


def tick(env, args, iters_done: int) -> None:
    """Once an iteration: the hit bank's curriculum level."""
    hits = getattr(env, "hits", None)
    if hits is not None and getattr(args, "hit_ramp_iters", 0) > 0:
        hits.level.fill_(min(1.0, max(0.0, iters_done / args.hit_ramp_iters)))
