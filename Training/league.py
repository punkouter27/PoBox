"""A self-play league for train_box.py --league: the learner boxes a pool of frozen opponents and an exploiter.

Opt-in; without --league train_box.py runs as before. Needs a two-fighter (spar or match) model. The learner is the
first fighter (a_); the second fighter's body is driven, ring by ring, by a member of the pool or by the exploiter:

    pool        frozen policies: snapshots of the learner (one at the start, then every --league-snapshot
                iterations), checkpoints (--league-pool PATH.pt) and the shipped brains of other boxers
                (--league-boxers NAME: Assets/_Project/RL_Models/NAME/<brain>.onnx, rebuilt as tools/onnx_to_pt.py
                does). Every member plays its best punch (no exploration noise), as the game plays it.
    PFSP        each ring draws its opponent at the end of every episode, with weight (1 - x)^p, x the learner's
                (decaying, smoothed) score against that member: prioritised fictitious self-play (AlphaStar), so an
                opponent that beats the learner is met more often and one it always beats is nearly retired.
    exploiter   a share of the rings (--league-exploit-share) is a second PPO learner that trains only against the
                current learner. Every --league-exploiter-reset iterations it joins the pool if it scored at least
                half against the learner, and starts again from the learner's weights.

The score of an episode: a knockdown decides it (the fighter whose step reward fell by the fall penalty against the
other's went down), else the fighter that landed more punches wins, else a draw (half). Punches are counted from the
env's own since_hit clock (0 on the step a fighter lands); nothing in the env changes.

A frozen member reads the observation it was trained on: one that reads fewer numbers than the env gives sees the
first ones, one that reads more sees zeros after them (as ppo.PPO.load(widen=True) treats a narrower brain).
"""

from __future__ import annotations

import copy
import os
from typing import Dict, List

import torch
import torch.nn as nn

HERE = os.path.dirname(os.path.abspath(__file__))
BRAINS = os.path.join(HERE, "..", "Assets", "_Project", "RL_Models")


class FrozenPolicy(nn.Module):
    """obs (raw) -> the actor's mean action. A policy that does not learn."""

    def __init__(self, actor: nn.Module, mean: torch.Tensor, std: torch.Tensor, clip: float = 10.0):
        super().__init__()
        self.actor = actor.eval()
        for p in self.actor.parameters():
            p.requires_grad_(False)
        self.register_buffer("mean", mean.detach().clone())
        self.register_buffer("std", std.detach().clone())
        self.clip = clip
        self.in_dim = int(mean.shape[0])

    @torch.no_grad()
    def forward(self, obs: torch.Tensor) -> torch.Tensor:
        D = obs.shape[-1]
        if D > self.in_dim:
            obs = obs[..., : self.in_dim]
        elif D < self.in_dim:
            obs = torch.cat([obs, obs.new_zeros(*obs.shape[:-1], self.in_dim - D)], -1)
        return self.actor(((obs - self.mean) / self.std).clamp(-self.clip, self.clip))

    @classmethod
    def from_ppo(cls, ppo) -> "FrozenPolicy":
        rms = ppo.obs_rms
        return cls(copy.deepcopy(ppo.model.actor), rms.mean, torch.sqrt(rms.var + 1e-8), ppo.cfg.obs_clip)

    @classmethod
    def from_checkpoint(cls, path: str, device: str) -> "FrozenPolicy":
        from ppo import mlp

        ck = torch.load(path, map_location=device, weights_only=False)
        sd = {k[6:]: v for k, v in ck["model"].items() if k.startswith("actor.")}
        actor = _actor_from(sd, mlp).to(device)
        rms = ck["obs_rms"]
        return cls(actor, rms["mean"].to(device), torch.sqrt(rms["var"].to(device) + 1e-8))

    @classmethod
    def from_onnx(cls, path: str, device: str) -> "FrozenPolicy":
        """A shipped brain (ppo.ExportPolicy's graph: actor weights, the normaliser's mean and std)."""
        import onnx
        import onnx.numpy_helper
        from ppo import mlp

        w = {i.name: torch.from_numpy(onnx.numpy_helper.to_array(i).copy()) for i in onnx.load(path).graph.initializer}
        sd = {k[6:]: v.float() for k, v in w.items() if k.startswith("actor.")}
        actor = _actor_from(sd, mlp).to(device)
        return cls(actor, w["mean"].float().to(device), w["std"].float().to(device))


def _actor_from(sd: Dict[str, torch.Tensor], mlp) -> nn.Module:
    """An actor MLP shaped by its weights (Linear layers at even indices, ELU between)."""
    ws = sorted((int(k.split(".")[0]), v) for k, v in sd.items() if k.endswith(".weight"))
    hidden = tuple(int(v.shape[0]) for _, v in ws[:-1])
    actor = mlp(int(ws[0][1].shape[1]), hidden, int(ws[-1][1].shape[0]))
    actor.load_state_dict(sd)
    return actor


def brain_path(name: str, brain: str = "match") -> str:
    """A shipped brain by boxer name (case as the folder; matched without case)."""
    if os.path.isdir(BRAINS):
        for d in os.listdir(BRAINS):
            if d.lower() == name.lower():
                p = os.path.join(BRAINS, d, f"{brain}.onnx")
                if os.path.exists(p):
                    return p
    raise SystemExit(f"[league] no {brain}.onnx for {name} under Assets/_Project/RL_Models")


def outcome(r_l, r_o, hits_l, hits_o, done, timeout, fall_penalty: float):
    """The learner's result of every ring that ended this step: (win, draw, loss) as floats, each (R,)."""
    ko = done & ~timeout
    gap = 0.5 * max(fall_penalty, 1e-6)
    went_l = ko & (r_l < r_o - gap)
    went_o = ko & (r_o < r_l - gap)
    on_points = done & ~went_l & ~went_o
    win = (went_o & ~went_l) | (on_points & (hits_l > hits_o))
    loss = (went_l & ~went_o) | (on_points & (hits_o > hits_l))
    draw = done & ~win & ~loss
    return win.float(), draw.float(), loss.float()


class League:
    """The opponents of rings [0, R_pool) are drawn from the pool; rings [R_pool, R) belong to the exploiter (which the
    trainer runs). Results are kept on the device: nothing here waits for the GPU but stats()."""

    def __init__(
        self,
        rings: int,
        device: str,
        exploit_rings: int = 0,
        max_size: int = 8,
        pfsp_p: float = 2.0,
        decay: float = 0.99,
        fall_penalty: float = 4.0,
        dt: float = 0.02,
        seed: int = 0,
    ):
        self.R, self.ex = rings, exploit_rings
        self.R_pool = rings - exploit_rings
        if self.R_pool < 1:
            raise SystemExit("[league] the exploiter share leaves no rings for the pool")
        self.device, self.cap, self.p, self.decay = device, max_size, pfsp_p, decay
        self.fall_penalty, self.dt = fall_penalty, dt
        self.members: List[dict] = []  # {"name", "policy", "protected"}
        # (cap + 1, 3): the learner's wins, draws, losses against each member; the last row is the exploiter.
        self.res = torch.zeros(max_size + 1, 3, device=device)
        self.games = torch.zeros(max_size + 1, device=device)  # undecayed, for the log
        self.assign = torch.zeros(rings, dtype=torch.long, device=device)
        self.assign[self.R_pool :] = max_size
        self.hits = torch.zeros(rings, 2, device=device)
        self.gen = torch.Generator(device=device)
        self.gen.manual_seed(seed)
        self.window = torch.zeros(3, device=device)

    # ---- the pool ----------------------------------------------------------------------------------------------
    def add(self, name: str, policy: FrozenPolicy, protected: bool = False) -> None:
        """A new member. A full pool first drops its least threatening unprotected member (the one the learner beats
        most); the starting snapshot and the opponents named on the command line are protected while any other is left."""
        if len(self.members) >= self.cap:
            w = self.weights()[: len(self.members)].tolist()
            cands = [i for i, m in enumerate(self.members) if not m["protected"]] or list(range(len(self.members)))
            drop = min(cands, key=lambda i: w[i])
            print(f"[league] {self.members[drop]['name']} leaves the pool", flush=True)
            # Rows above it move down one; the rings that met it draw again at their next episode.
            del self.members[drop]
            keep = [i for i in range(self.cap) if i != drop] + [self.cap]
            self.res = torch.cat([self.res[keep[:-1]], torch.zeros(1, 3, device=self.device), self.res[-1:]])
            self.games = torch.cat([self.games[keep[:-1]], torch.zeros(1, device=self.device), self.games[-1:]])
            pool = self.assign[: self.R_pool]
            pool = torch.where(pool == drop, torch.zeros_like(pool), torch.where(pool > drop, pool - 1, pool))
            self.assign[: self.R_pool] = pool
        self.members.append({"name": name, "policy": policy, "protected": protected})
        if len(self.members) == 1:
            self.assign[: self.R_pool] = 0

    def weights(self) -> torch.Tensor:
        """PFSP weight of every pool slot, (cap,): (1 - x)^p, x = (wins + draws / 2 + 1) / (games + 2); 0 for empty."""
        r = self.res[: self.cap]
        x = (r[:, 0] + 0.5 * r[:, 1] + 1.0) / (r.sum(1) + 2.0)
        w = (1.0 - x).clamp_min(1e-3) ** self.p
        live = torch.arange(self.cap, device=self.device) < len(self.members)
        return w * live.float()

    def sample(self, n: int) -> torch.Tensor:
        return torch.multinomial(self.weights(), n, replacement=True, generator=self.gen)

    # ---- the rollout -------------------------------------------------------------------------------------------
    @torch.no_grad()
    def act(self, obs_pool: torch.Tensor) -> torch.Tensor:
        """The pool rings' opponent actions, (R_pool, A): every member answers every ring and each ring takes its own
        member's (no gather by group, so no host sync; the pool is small and the MLPs are cheap beside the physics)."""
        a = self.assign[: self.R_pool, None]
        out = None
        for j, m in enumerate(self.members):
            aj = m["policy"](obs_pool)
            out = aj if out is None else torch.where(a == j, aj, out)
        return out

    @torch.no_grad()
    def record(self, rew: torch.Tensor, done: torch.Tensor, timeout: torch.Tensor, since_hit: torch.Tensor) -> None:
        """After env.step: rew, done, timeout (R, 2) per fighter (learner first); since_hit (R, 2) from the env (0 where
        a fighter landed this step). Scores the rings that ended and draws their next opponent."""
        self.hits += (since_hit < 0.5 * self.dt).float()
        d, t = done[:, 0].bool(), timeout[:, 0].bool()
        win, draw, loss = outcome(rew[:, 0], rew[:, 1], self.hits[:, 0], self.hits[:, 1], d, t, self.fall_penalty)
        res = torch.stack([win, draw, loss], -1)
        self.res.index_add_(0, self.assign, res)
        self.games.index_add_(0, self.assign, d.float())
        self.window += res.sum(0)
        self.hits *= (~d).float()[:, None]
        if self.members:
            new = self.sample(self.R_pool)
            pool = self.assign[: self.R_pool]
            self.assign[: self.R_pool] = torch.where(d[: self.R_pool], new, pool)

    def tick(self) -> None:
        """Once an iteration: old results count for less, so the weights follow the learner as it changes."""
        self.res *= self.decay

    def exploiter_score(self) -> float:
        """The exploiter's score against the learner since its last reset (0.5 with no games)."""
        r = self.res[self.cap]
        n = float(r.sum())
        return 0.5 if n <= 0.0 else float((r[2] + 0.5 * r[1]) / n)

    def reset_exploiter_record(self) -> None:
        self.res[self.cap] = 0.0

    def stats(self) -> Dict[str, float]:
        w = self.window
        n = float(w.sum())
        out = {
            "league_size": float(len(self.members)),
            "league_score": float((w[0] + 0.5 * w[1]) / n) if n > 0 else 0.5,
            "league_draws": float(w[1] / n) if n > 0 else 0.0,
        }
        if self.ex:
            out["league_exploiter_score"] = self.exploiter_score()
        self.window.zero_()
        return out

    def describe(self) -> str:
        w = self.weights()[: len(self.members)]
        w = (w / w.sum().clamp_min(1e-9)).tolist()
        g = self.games.tolist()
        return ", ".join(f"{m['name']} {wi:.0%} ({int(gi)})" for m, wi, gi in zip(self.members, w, g))

    # ---- persistence -------------------------------------------------------------------------------------------
    def state_dict(self) -> dict:
        return {
            "members": [
                {"name": m["name"], "protected": m["protected"], "policy": m["policy"].state_dict(),
                 "actor_shape": _shape_of(m["policy"])}
                for m in self.members
            ],
            "res": self.res.cpu(),
            "games": self.games.cpu(),
        }  # fmt: skip

    def load_state_dict(self, sd: dict) -> None:
        from ppo import mlp

        self.members = []
        for m in sd["members"]:
            inp, hidden, out = m["actor_shape"]
            pol = FrozenPolicy(mlp(inp, tuple(hidden), out), torch.zeros(inp), torch.ones(inp)).to(self.device)
            pol.load_state_dict(m["policy"])
            self.members.append({"name": m["name"], "policy": pol, "protected": m["protected"]})
        self.members = self.members[: self.cap]
        n = len(self.members)
        self.res.zero_()
        self.games.zero_()
        self.res[:n] = sd["res"][:n].to(self.device)
        self.games[:n] = sd["games"][:n].to(self.device)
        self.assign[: self.R_pool] = self.sample(self.R_pool)


def _shape_of(p: FrozenPolicy):
    lin = [m for m in p.actor if isinstance(m, nn.Linear)]
    return (lin[0].in_features, [m.out_features for m in lin[:-1]], lin[-1].out_features)


class LeagueTrainer:
    """What train_box.py holds for --league: the league, the exploiter's PPO, and the snapshots.

    lg = LeagueTrainer(env, learner, args, device)
    act = lg.actions(obs, learner_action)          # (N * 2, A): the learner's, then each ring's opponent's
    obs, rew, done, timeout = env.step(act)
    lg.record(rew, done, timeout)
    ... learner.update(...)
    lg.end_iter(it, obs)                           # the exploiter's update, snapshots, the exploiter's reset
    """

    def __init__(
        self,
        env,
        learner,
        device: str,
        pool: List[str] = (),
        boxers: List[str] = (),
        brain: str = "match",
        exploit_share: float = 0.125,
        snapshot_every: int = 50,
        exploiter_reset: int = 200,
        max_size: int = 8,
        pfsp_p: float = 2.0,
        seed: int = 0,
    ):
        if env.K != 2 or not getattr(env, "spar", False):
            raise SystemExit("--league needs a model with two fighters who box (a spar or match model)")
        from ppo import PPO, PPOConfig

        self.env, self.learner, self.device = env, learner, device
        N = env.N
        ex = int(round(N * exploit_share)) if exploit_share > 0.0 else 0
        ex = min(max(ex, 1 if exploit_share > 0.0 else 0), N - 1)
        self.league = League(
            N,
            device,
            exploit_rings=ex,
            max_size=max_size,
            pfsp_p=pfsp_p,
            fall_penalty=float(getattr(env, "fall_penalty", 4.0)),
            dt=float(env.dt),
            seed=seed,
        )
        self.snapshot_every, self.exploiter_reset = snapshot_every, exploiter_reset
        self.league.add("self@start", FrozenPolicy.from_ppo(learner), protected=True)
        for name in boxers:
            self.league.add(f"{name}:{brain}", FrozenPolicy.from_onnx(brain_path(name, brain), device), protected=True)
        for path in pool:
            load = FrozenPolicy.from_onnx if path.endswith(".onnx") else FrozenPolicy.from_checkpoint
            self.league.add(os.path.splitext(os.path.basename(path))[0], load(path, device), protected=True)
        self.exploiter = None
        if ex:
            c = learner.cfg
            cfg = PPOConfig(
                **{**c.__dict__, "minibatches": max(1, round(c.steps_per_env * ex / 24576)), "sym_coef": 0.0}
            )
            self.exploiter = PPO(
                learner.model.actor[0].in_features, learner.model.actor[-1].out_features, ex, device, cfg
            )
            self._exploiter_from_learner()
        print(f"[league] {N - ex} rings against the pool, {ex} against the exploiter; pool: {self.league.describe()}")

    def _exploiter_from_learner(self) -> None:
        x, l = self.exploiter, self.learner
        x.model.load_state_dict(l.model.state_dict())
        x.obs_rms.load_state_dict(
            {k: (v.clone() if torch.is_tensor(v) else v) for k, v in l.obs_rms.state_dict().items()}
        )
        x.opt = torch.optim.Adam(x.model.parameters(), lr=l.cfg.lr)
        x.cfg.lr = l.cfg.lr

    def actions(self, obs: torch.Tensor, learner_act: torch.Tensor) -> torch.Tensor:
        N, A, Rp = self.env.N, learner_act.shape[-1], self.league.R_pool
        o = obs.view(N, 2, -1)[:, 1]
        opp = self.league.act(o[:Rp])
        if self.exploiter is not None:
            opp = torch.cat([opp, self.exploiter.act(o[Rp:])], 0)
        return torch.stack([learner_act, opp], 1).reshape(N * 2, A)

    def record(self, rew: torch.Tensor, done: torch.Tensor, timeout: torch.Tensor) -> None:
        N = self.env.N
        r, d, t = rew.view(N, 2), done.view(N, 2), timeout.view(N, 2)
        since = getattr(self.env, "since_hit", None)
        if since is None:
            since = torch.full((N, 2), 99.0, device=self.device)
        self.league.record(r, d, t, since)
        if self.exploiter is not None:
            Rp = self.league.R_pool
            self.exploiter.record(r[Rp:, 1], d[Rp:, 1], t[Rp:, 1])

    def end_iter(self, it: int, obs: torch.Tensor) -> Dict[str, float]:
        out: Dict[str, float] = {}
        if self.exploiter is not None:
            o = obs.view(self.env.N, 2, -1)[:, 1][self.league.R_pool :]
            st = self.exploiter.update(o)
            out.update({"exploiter_kl": st["kl"], "exploiter_std": st["action_std"]})
        self.league.tick()
        done_iters = it + 1
        if self.snapshot_every > 0 and done_iters % self.snapshot_every == 0:
            self.league.add(f"self@{done_iters}", FrozenPolicy.from_ppo(self.learner))
            print(f"[league] snapshot self@{done_iters}; pool: {self.league.describe()}", flush=True)
        if self.exploiter is not None and self.exploiter_reset > 0 and done_iters % self.exploiter_reset == 0:
            score = self.league.exploiter_score()
            if score >= 0.5:
                self.league.add(f"exploiter@{done_iters}", FrozenPolicy.from_ppo(self.exploiter))
            print(f"[league] exploiter scored {score:.2f} against the learner"
                  f"{' and joins the pool' if score >= 0.5 else ''}; it starts again from the learner", flush=True)  # fmt: skip
            self.league.reset_exploiter_record()
            self._exploiter_from_learner()
        return out

    def stats(self) -> Dict[str, float]:
        return self.league.stats()

    def save(self, folder: str) -> None:
        sd = {"league": self.league.state_dict()}
        if self.exploiter is not None:
            sd["exploiter"] = {
                "model": self.exploiter.model.state_dict(),
                "obs_rms": self.exploiter.obs_rms.state_dict(),
            }
        torch.save(sd, os.path.join(folder, "league.pt"))

    def load(self, path: str) -> None:
        sd = torch.load(path, map_location=self.device, weights_only=False)
        named = [m for m in self.league.members if m["name"] != "self@start"]  # this run's command-line opponents
        self.league.load_state_dict(sd["league"])
        have = {m["name"] for m in self.league.members}
        for m in named:
            if m["name"] not in have:
                self.league.add(m["name"], m["policy"], protected=True)
        if self.exploiter is not None and "exploiter" in sd:
            self.exploiter.model.load_state_dict(sd["exploiter"]["model"])
            self.exploiter.obs_rms.load_state_dict(sd["exploiter"]["obs_rms"])
        print(f"[league] resumed from {path}; pool: {self.league.describe()}", flush=True)
