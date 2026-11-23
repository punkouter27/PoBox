"""StepGraph: a whole env.step (the warp physics and the torch reward, reset and observation) as one CUDA graph.

Eagerly a match step is about 1,250 small kernel launches, and the trainer's one Python thread spends ~18 us on each:
on an RTX 2060 the GPU sat 30% idle waiting for it (benchmark 2026-11-22). Replayed as a graph it is one launch.

The step rebinds its state attributes (self.cool = ...), which a graph cannot follow, so the state is pinned: during
capture every rebound tensor attribute (and tensor in a dict attribute, like _acc) is copied back into the tensor it
held before the step, and the attribute is pointed back at it. Python numbers on the env (effort_scale, ...) are baked
into the graph, so a change to any of them recaptures it. The first call, and the call that recaptures, run eagerly.
The step must never read the GPU back (.item(), `if tensor`): capture fails on that, and StepGraph falls back to eager.

Warp is told the capture is external (wp.capture_begin(external=True)) so it defers freeing its temporaries, and is put
on the capture stream before the capture begins (switching streams mid-capture syncs).

The outputs are the graph's own tensors, overwritten by the next replay: use them before stepping this env again.
"""

from __future__ import annotations

import torch
import warp as wp


def _plain(v) -> bool:
    return isinstance(v, (bool, int, float, str)) or v is None


class StepGraph:
    def __init__(self, env):
        self.env = env
        self.graph = None
        self.key = None
        self.pins = {}
        self.captures = 0
        self.failed = False
        self.calls = 0
        self._last_capture_call = -10
        self.stream = torch.cuda.Stream(device=env.device)

    # ---- the env's state ---------------------------------------------------------------------------
    def _scalars(self):
        return tuple(sorted((k, v) for k, v in vars(self.env).items() if _plain(v)))

    def _tensors(self):
        d = vars(self.env)
        flat = {("a", k): v for k, v in d.items() if isinstance(v, torch.Tensor)}
        for k, v in d.items():
            if isinstance(v, dict) and v and all(isinstance(x, torch.Tensor) for x in v.values()):
                flat.update({("d", k, kk): x for kk, x in v.items()})
        return flat

    def _get(self, key):
        return getattr(self.env, key[1]) if key[0] == "a" else getattr(self.env, key[1])[key[2]]

    def _set(self, key, val) -> None:
        if key[0] == "a":
            setattr(self.env, key[1], val)
        else:
            getattr(self.env, key[1])[key[2]] = val

    def _restore(self, pins, clone: bool) -> None:
        """Point every pinned attribute back at its pinned tensor, copying in what was left there."""
        moved = []
        for key, t0 in pins.items():
            t1 = self._get(key)
            if t1 is t0:
                continue
            if not isinstance(t1, torch.Tensor) or t1.shape != t0.shape or t1.dtype != t0.dtype:
                raise RuntimeError(f"step graph: {key} changed shape or dtype; it cannot be pinned")
            moved.append((key, t0, t1.clone() if clone else t1))  # cloned first: one may alias another's pin
        for key, t0, t1 in moved:
            t0.copy_(t1)
            self._set(key, t0)

    # ---- capture -----------------------------------------------------------------------------------
    def _capture(self, action: torch.Tensor) -> None:
        e = self.env
        self.a_in = torch.zeros_like(action)
        pins = self._tensors()
        g = torch.cuda.CUDAGraph()
        saved = (e._step_graph, e._fwd_graph)
        e._step_graph = e._fwd_graph = None  # warp's own graphs cannot launch inside a capture: direct launches
        torch.cuda.synchronize()
        ws = wp.stream_from_torch(self.stream)
        try:
            with wp.ScopedStream(ws), torch.cuda.graph(g, stream=self.stream):
                wp.capture_begin(stream=ws, external=True)
                try:
                    self.out = tuple(e.step(self.a_in))
                    self._restore(pins, clone=True)
                finally:
                    self._wp_graph = wp.capture_end(stream=ws)
        except Exception as ex:  # an optimisation, never a requirement
            for key, t0 in pins.items():  # nothing ran during the capture: the pinned tensors hold the state
                self._set(key, t0)
            self.failed = True
            print(f"[step-graph] capture unavailable ({str(ex).splitlines()[0]}); stepping eagerly", flush=True)
            return
        finally:
            e._step_graph, e._fwd_graph = saved
        self.pins = self._tensors()  # what the step creates stays pointing at the graph's own memory
        self.graph = g
        self.key = self._scalars()
        self.captures += 1
        if self.calls - self._last_capture_call <= 2:
            print(
                f"[step-graph] recaptured on consecutive steps ({self.captures} captures): an env number "
                "changes every step, so the graph is not saving anything",
                flush=True,
            )
        self._last_capture_call = self.calls

    def __call__(self, action: torch.Tensor):
        self.calls += 1
        e = self.env
        if self.failed:
            return e.step(action)
        if self.graph is not None and self._scalars() == self.key:
            self._restore(self.pins, clone=False)  # anything eager (a reset, a tool) may have rebound the state
            self.a_in.copy_(action)
            self.graph.replay()
            return self.out
        out = tuple(o.clone() for o in e.step(action))  # eager this once; captured for the next
        self._capture(action)
        return out
