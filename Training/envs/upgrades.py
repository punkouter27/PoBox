"""The command-line side of the opt-in trainer upgrades of 2026-11-25, shared by train_box.py and train_distill.py.

    --domain-rand (+ --dr-*)   envs/randomize.py, ranges in config/domain_rand.json
    --muscle hill (+ --muscle-*) envs/muscle.py; the game runs it from the boxer config's "muscle" block
    --energy-w W               the mechanical work sum |tau omega| charged at W (reward config's mech_work)
    --hit-bank PATH (+ --hit-*) envs/hit_bank.py, a bank written by tools/hits_to_bank.py

Every one is off by default: with none given, kwargs() is {} and the envs are exactly as before.
"""

from __future__ import annotations

import argparse
import os


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

    # ---- 2026-11-25, the progressive batch: hard physical limits, the rungs above a walk, and encoder reuse ----
    p = ap.add_argument_group("progressive (2026-11-25, all off by default)")
    p.add_argument("--gravity-scale", type=float, default=None, help="+- share of g each world feels (0 = Earth)")
    p.add_argument("--torque-cap", type=float, default=None, help="share of each drive's force limit the motor may use")
    p.add_argument("--torque-var", type=float, default=None, help="+- share of the torque cap, per world")
    p.add_argument("--act-lag", type=float, default=None, help="motor bandwidth, Hz (0 = the drive follows at once)")
    p.add_argument("--lag-var", type=float, default=None, help="+- share of the motor bandwidth, per fighter")
    p.add_argument("--soft-limit", type=float, default=None, help="rad inside a joint's range where its stop gives")
    p.add_argument("--rom-w", type=float, default=None, help="charge per rad pressed into the last of a joint's range")
    p.add_argument(
        "--terrain",
        default="",
        help="footwork: floor classes from config/progression.json, re-drawn every reset (comma list to keep some; "
        "empty = the one flat floor)",
    )
    p.add_argument("--terrain-bump", type=float, default=None, help="--terrain: the uneven-ground impulse, m/s")
    p.add_argument("--nav-share", type=float, default=None, help="footwork: share of walks that navigate to a goal")
    p.add_argument("--nav-obstacles", type=int, default=None, help="--nav-share: most cubes parked across the path")
    p.add_argument("--sprint-share", type=float, default=None, help="footwork: share of walks that ask for a sprint")
    p.add_argument("--sprint-w", type=float, default=None, help="--sprint-share: pay for a stride past 0.35 s")
    p.add_argument("--momentum-w", type=float, default=None, help="footwork: charge per (rad/s^2) of yaw jerk")
    p.add_argument("--lean-w", type=float, default=None, help="footwork: charge for missing the centripetal lean")
    p.add_argument("--turn-accel", type=float, default=None, help="rad/s^2 the yaw command may change at (12)")
    p.add_argument(
        "--curriculum",
        default="",
        help="footwork: the rung ladder of config/progression.json; auto = start at balance and climb, or name a rung "
        "(balance, walk, sprint, navigate) to start there",
    )
    p.add_argument("--shared-encoder", action="store_true", help="one trunk behind the actor and the critic")
    p.add_argument("--freeze-layers", type=int, default=0, help="hold the first N encoder layers fixed")
    p.add_argument(
        "--freeze-until", type=int, default=0, help="--freeze-layers: let them go after this many iterations"
    )
    p.add_argument("--reuse-trunk", default="", help="copy the encoder's weights out of this checkpoint first")
    p.add_argument("--mirror-aug", type=float, default=0.0, help="share of each rollout trained mirrored as well")


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
    # The hard physical limits (envs/constraints.py), when any of them is stricter than the game's body.
    over = {
        "gravity": getattr(args, "gravity_scale", None),
        "torque_cap": getattr(args, "torque_cap", None),
        "torque_var": getattr(args, "torque_var", None),
        "act_lag_hz": getattr(args, "act_lag", None),
        "lag_var": getattr(args, "lag_var", None),
        "soft_limit": getattr(args, "soft_limit", None),
        "rom_w": getattr(args, "rom_w", None),
    }
    if any(v is not None for v in over.values()):
        from .limits import PhysRanges

        out["phys"] = PhysRanges.from_config(**over)
    return out


FOOTWORK_KEYS = (
    ("terrain", "terrain"),
    ("terrain_bump", "terrain_bump"),
    ("nav_share", "nav_share"),
    ("nav_obstacles", "nav_obstacles"),
    ("sprint_share", "sprint_share"),
    ("sprint_w", "sprint_w"),
    ("momentum_w", "momentum_w"),
    ("lean_w", "lean_w"),
    ("turn_accel", "turn_accel"),
)


def footwork_kwargs(args) -> dict:
    """The footwork rung's own knobs (terrain, navigation, sprint, momentum); {} when none was asked for."""
    out = {}
    for arg, kw in FOOTWORK_KEYS:
        v = getattr(args, arg, None)
        if v is not None and v != "":
            out[kw] = v
    return out


def ppo_kwargs(args) -> dict:
    """PPOConfig settings the progressive batch adds (ppo.py): a shared encoder and mirror augmentation."""
    return {
        "shared_encoder": bool(getattr(args, "shared_encoder", False)),
        "mirror_aug": float(getattr(args, "mirror_aug", 0.0) or 0.0),
    }


class Progressive:
    """The encoder reuse and the rung ladder of a run, applied once the env and the learners exist.

    attach() copies the encoder (--reuse-trunk), holds its first layers (--freeze-layers, let go at --freeze-until
    iterations) and starts the curriculum (--curriculum). update() is called once an iteration, with the same stats dict
    the trainer logs (get_stats() zeroes its counters, so it is never called twice), and moves the run up or down the
    ladder as the numbers earn it, writing the rung's knobs into the live env.
    """

    def __init__(self, env, learners, args):
        self.env, self.learners, self.args = env, learners, args
        self.sched = None
        self.freeze_until = 0
        if getattr(args, "reuse_trunk", ""):
            import hierarchy

            for ppo in learners:
                copied = hierarchy.load_trunk(ppo, args.reuse_trunk)
                print(
                    f"[hierarchy] {os.path.basename(args.reuse_trunk)}: encoder copied ({len(copied)} layers)",
                    flush=True,
                )
        if getattr(args, "freeze_layers", 0) > 0:
            import hierarchy

            frozen = []
            for ppo in learners:
                frozen = hierarchy.freeze_prefix(ppo, args.freeze_layers)
            self.freeze_until = int(getattr(args, "freeze_until", 0))
            print(
                f"[hierarchy] {len(frozen)} encoder layers held"
                f"{'' if self.freeze_until else ' for the whole run'}; {hierarchy.describe(learners[0])}",
                flush=True,
            )
        if getattr(args, "curriculum", ""):
            from . import curriculum as cur

            self.sched = cur.from_args(cur.ladder(), args.curriculum)
            applied, unknown = cur.apply(env, self.sched.rung.overrides)
            print(
                f"[curriculum] starting at {self.sched.rung.name}: {', '.join(applied) or 'no knob'}"
                + (f" (not on this env: {', '.join(unknown)})" if unknown else ""),
                flush=True,
            )

    def update(self, stats: dict, iteration: int) -> str:
        """One iteration: let a held encoder go, and move the run along its ladder; returns a line for the log."""
        notes = []
        if self.freeze_until and iteration >= self.freeze_until:
            import hierarchy

            for ppo in self.learners:
                hierarchy.unfreeze(ppo)
            notes.append(f"[hierarchy] encoder let go at iteration {iteration}")
            self.freeze_until = 0
        if self.sched is not None:
            from . import curriculum as cur

            what, over = self.sched.update(stats, iteration)
            if what:
                applied, unknown = cur.apply(self.env, over)
                notes.append(
                    f"[curriculum] {what} to {self.sched.rung.name}: {', '.join(applied) or 'no knob'}"
                    + (f" (not on this env: {', '.join(unknown)})" if unknown else "")
                )
            elif iteration % 10 == 0:
                notes.append(f"[curriculum] {self.sched.line(stats)}")
        return " | ".join(notes)


def attach(env, learners, args) -> Progressive:
    """Make the progressive state of a run and hang it on the env (tick() then leaves it alone)."""
    state = Progressive(env, learners, args)
    env._progressive = state
    return state


def tick(env, args, iters_done: int) -> None:
    """Once an iteration: the hit bank's curriculum level."""
    hits = getattr(env, "hits", None)
    if hits is not None and getattr(args, "hit_ramp_iters", 0) > 0:
        hits.level.fill_(min(1.0, max(0.0, iters_done / args.hit_ramp_iters)))
