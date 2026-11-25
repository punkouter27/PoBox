"""Hierarchical reuse between the rungs: freeze the low-level encoder, or carry it into the next stage.

The ladder is trained one skill at a time (get-up -> footwork -> match -> boxing), and each rung begins from the previous
one's policy (AGENTS.md 2.3, warm start). Two things make that a hierarchy rather than a restart (train_box.py):

  --freeze-layers N   the first N linear layers of the actor and the critic (the encoder that turns proprioception into
                      a body state) are held fixed for the new rung: it learns its new output on top of what locomotion
                      already knows, then the encoder is let go (--freeze-until).
  --reuse-trunk CKPT  the encoder's weights are copied out of an older checkpoint before training starts, so a rung can
                      reuse a stage's trunk even when its heads differ in size.
  --shared-encoder    the actor and the critic share one trunk (PPOConfig.shared_encoder): one encoder serves both, half
                      the parameters to fit, and that is the trunk a later rung reuses.

Nothing here changes what a policy means: the exported ONNX is the actor alone, as before.
"""

from __future__ import annotations

import torch
import torch.nn as nn

from ppo import PPO


def linear_layers(module: nn.Module) -> list:
    """Every Linear in a module, in forward order."""
    return [m for m in module.modules() if isinstance(m, nn.Linear)]


def _nets(ppo: PPO) -> list:
    """The networks whose encoders are held: one when the trunk is shared (freezing it freezes both heads)."""
    if getattr(ppo.model, "shared", False):
        return [ppo.model.actor]
    out = []
    for name in ("actor", "critic"):
        net = getattr(ppo.model, name, None)
        if net is not None:
            out.append(net)
    return out


def _head_names(ppo: PPO) -> set:
    """The module names of the actor's and the critic's output layer: the only Linears that are not encoder."""
    heads = set()
    for name in ("actor", "critic"):
        net = getattr(ppo.model, name, None)
        layers = linear_layers(net) if net is not None else []
        if not layers:
            continue
        for sub, mod in net.named_modules():
            if mod is layers[-1]:
                heads.add(f"{name}.{sub}" if sub else name)
    return heads


def freeze_prefix(ppo: PPO, n: int) -> list:
    """Hold the first n encoder layers of every network fixed; returns what was frozen, by module name."""
    frozen = []
    for net in _nets(ppo):
        for i, lin in enumerate(linear_layers(net)[: max(0, int(n))]):
            lin.requires_grad_(False)
            frozen.append(f"{type(net).__name__}.{i}")
    return frozen


def freeze_trunk(ppo: PPO) -> list:
    """A shared encoder held fixed (a no-op when the actor and the critic do not share one)."""
    trunk = getattr(ppo.model, "trunk", None)
    if trunk is None:
        return []
    for p in trunk.parameters():
        p.requires_grad_(False)
    return [f"trunk.{i}" for i, _ in enumerate(linear_layers(trunk))]


def unfreeze(ppo: PPO) -> None:
    for p in ppo.model.parameters():
        p.requires_grad_(True)


def trainable(ppo: PPO) -> tuple:
    """(trainable, total) parameters: what an encoder held fixed leaves for the rung to fit."""
    total = sum(p.numel() for p in ppo.model.parameters())
    live = sum(p.numel() for p in ppo.model.parameters() if p.requires_grad)
    return live, total


def load_trunk(ppo: PPO, path: str, n: int = 0) -> list:
    """Copy the encoder out of a checkpoint wherever the shapes agree; n = 0 copies every layer that does.

    The head (each network's last Linear) is never copied: another stage's action head is a different problem. A shared
    trunk and a separate pair are cross-readable (actor.i <-> trunk.i), so a rung may reuse either kind's encoder."""
    ck = torch.load(path, map_location=ppo.device, weights_only=False)
    sd = ck.get("model", ck)
    heads = _head_names(ppo)
    copied = []
    for mod_name, mod in ppo.model.named_modules():
        if not isinstance(mod, nn.Linear) or mod_name in heads:
            continue
        if n and len(copied) >= n:
            break
        aliases = [mod_name]
        if mod_name.startswith("actor."):
            aliases.append("trunk." + mod_name[len("actor.") :])
        if mod_name.startswith("trunk."):
            aliases.append("actor." + mod_name[len("trunk.") :])
        src = next((sd[a + ".weight"] for a in aliases if a + ".weight" in sd), None)
        if src is None or src.shape != mod.weight.shape:
            continue
        bias = next((sd[a + ".bias"] for a in aliases if a + ".bias" in sd), None)
        with torch.no_grad():
            mod.weight.copy_(src.to(mod.weight.device))
            if bias is not None and mod.bias is not None and bias.shape == mod.bias.shape:
                mod.bias.copy_(bias.to(mod.bias.device))
        copied.append(mod_name)
    return copied


def describe(ppo: PPO) -> str:
    """One line for the run's log: the encoder's shape and how much of the model is still learning."""
    live, total = trainable(ppo)
    trunk = getattr(ppo.model, "trunk", None)
    shape = "shared " + "-".join(str(l.out_features) for l in linear_layers(trunk)) if trunk is not None else "separate"
    return f"encoder {shape}; learning {live:,} of {total:,} parameters ({live / max(1, total):.0%})"
