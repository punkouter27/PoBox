"""Walk-on moves and winners' celebrations: a few seconds each from the CMU database, for the boxers.

    python tools/intro_clips.py              fetch what is missing, retarget, pick each take's best window, write the game's file
    python tools/intro_clips.py --survey     also print every candidate take's best window, written or not

Nothing here is trained. The game plays these clips straight onto the body (IntroMoves.cs) while the
walk-on has the physics stopped: each frame it writes the pose into qpos and runs mj_kinematics, as the
instant replay does. So a clip only has to look right, not to balance.

Each take is retargeted by retarget_clips.retarget (the 21 joints, root in leg lengths), then the window
of WINDOW seconds is picked that moves the arms most while the pelvis stays within a step of where it
started and keeps facing about the same way: a boxer in its corner has the ropes behind it. The window
is put back at the origin facing +x, which the game turns to the boxer's own corner.

Writes Assets/_Project/Data/Resources/IntroClips.bytes (little-endian), read by IntroClips.cs:
    "PBIC"  int32 version (1)  float32 fps  int32 joints  joints x (int32 length, UTF-8 name)
    int32 clips  clips x (int32 length, UTF-8 name, int32 frames, frames x (7 + joints) float32)
    a row: root x, y, z in leg lengths, root quaternion (w x y z), the joint angles in radians.
The game scales the root by the body's legs and puts the lower sole on the floor every frame (place() here).

The celebrations (names starting celebrate_) are different: the winner is held standing where it won while the
loser is still simulated (MjRing.HoldPose), so only their arms and trunk are used, over the winner's stance. Their
window is the take's busiest for the arms wherever the pelvis goes, and the legs are not checked.
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import sys
import urllib.request

import mujoco
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from retarget_clips import FPS, HERE, URL, check, place, retarget  # noqa: E402

REPO = os.path.dirname(HERE)
OUT = os.path.join(REPO, "Assets", "_Project", "Data", "Resources", "IntroClips.bytes")
WINDOW = 5.0  # seconds: the walk-on's length (Bout.walkOnSeconds)
MAX_TRAVEL = 0.45  # leg lengths the pelvis may wander from where the window starts
MAX_TURN = 50.0  # degrees it may turn away from where it faced
# name in the game: (CMU take, what the database calls it)
CLIPS = {
    "shadowbox_a": ("14_01", "boxing"),
    "shadowbox_b": ("14_03", "boxing"),
    "shadowbox_c": ("13_17", "boxing"),
    "shadowbox_d": ("15_13", "boxing"),
    "shadowbox_e": ("14_02", "boxing"),
    "combos_a": ("80_10", "boxing"),
    "combos_b": ("143_23", "punching"),
}
# Looked at with --survey and left out: 17_10 (boxing) walks off in every window; 79_94 (flexing) leans back past the
# hips' range (shins 34 degrees off); 141_16 (wave hello) is 2.5 s, with the ankles out of range throughout.
# 79_08 (boxing) is 3.7 s; 13_18 (boxing) moves the arms least of the boxing takes.
SURVEY = {"13_18": "boxing", "79_08": "boxing"}
CELEBRATE = "celebrate_"
# The winner's celebration, arms and trunk only (owner, 2026-11-23). Looked at and left out: 18_15, 19_15 and 21_01
# (the same chicken dance, less of it), 05_12 and 49_09 (ballet, slow arms).
CELEBRATIONS = {
    CELEBRATE + "chicken": ("20_01", "chicken dance"),
    CELEBRATE + "wave": ("13_26", "direct traffic, wave"),
    CELEBRATE + "jacks": ("14_06", "jumping jacks, jog, squats, side twists, stretches"),
}
ARMS = ("shoulder", "elbow")


def heading(quat: np.ndarray) -> np.ndarray:
    """The pelvis's yaw from its quaternion (w x y z), radians: where its x axis points on the floor."""
    w, x, y, z = quat[..., 0], quat[..., 1], quat[..., 2], quat[..., 3]
    return np.arctan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z))


def retarget_wrapped(path: str, order: list, lower: np.ndarray, upper: np.ndarray):
    """retarget() with every angle, every frame, taken the way round that lies in its joint's range, then held inside it.

    retarget() unwraps an angle from wherever the take's first frame puts it, and a boxing guard puts the
    upper arm near straight ahead, where the shoulder's first angle is undefined and jumps: 14_01's right
    shoulder comes out a whole turn away from its range, and the clamp then bends the arm wrong. Each angle
    is moved by whole turns to its range's middle, and a shoulder takes whichever of its two solutions,
    (x, z) or (x + pi, pi - z), point the upper arm the same way, is less outside its range. The walking
    clips never go there; this is kept here so walk_turn.npz, which training reads, stays exactly as it was.
    """
    clip, dirs, _ = retarget(path, order, lower - 100.0, upper + 100.0)
    joints = clip[:, 7:].astype(np.float64)
    middle = 0.5 * (lower + upper)

    def wrap(q, i):
        return q - 2 * np.pi * np.round((q - middle[i]) / (2 * np.pi))

    def outside(q, i):
        return np.maximum(lower[i] - q, 0.0) + np.maximum(q - upper[i], 0.0)

    for i in range(len(order)):
        joints[:, i] = wrap(joints[:, i], i)
    for s in "lr":
        x, z = order.index(f"shoulder_x_{s}"), order.index(f"shoulder_z_{s}")
        ax, az = wrap(joints[:, x] + np.pi, x), wrap(np.pi - joints[:, z], z)
        other = outside(ax, x) + outside(az, z) < outside(joints[:, x], x) + outside(joints[:, z], z)
        joints[other, x], joints[other, z] = ax[other], az[other]
    clipped = float(np.mean((joints < lower - 1e-6) | (joints > upper + 1e-6)))
    clip[:, 7:] = joints.clip(lower, upper)
    return clip, dirs, clipped


def best_window(clip: np.ndarray, order: list, anywhere: bool = False):
    """The start frame of the window that moves the arms most within the travel and turn limits (none when
    anywhere: a celebration uses only the arms), and its numbers."""
    n = int(WINDOW * FPS)
    if len(clip) < n:
        return None
    arms = [7 + i for i, j in enumerate(order) if j.split("_")[0] in ARMS]
    speed = np.abs(np.diff(clip[:, arms], axis=0)).sum(1) * FPS  # rad/s summed over the arm joints
    yaw = np.unwrap(heading(clip[:, 3:7]))
    best = None
    for s in range(0, len(clip) - n, FPS // 5):
        xy = clip[s : s + n, :2] - clip[s, :2]
        travel = float(np.linalg.norm(xy, axis=1).max())
        turn = float(np.degrees(np.abs(yaw[s : s + n] - yaw[s]).max()))
        if not anywhere and (travel > MAX_TRAVEL or turn > MAX_TURN):
            continue
        score = float(speed[s : s + n - 1].mean())
        if best is None or score > best[1]:
            best = (s, score, travel, turn)
    return best


def zeroed(window: np.ndarray) -> np.ndarray:
    """The window moved to start at the origin facing +x (its height kept)."""
    out = window.copy()
    yaw = heading(window[0, 3:7])
    c, s = np.cos(-yaw), np.sin(-yaw)
    xy = window[:, :2] - window[0, :2]
    out[:, 0], out[:, 1] = c * xy[:, 0] - s * xy[:, 1], s * xy[:, 0] + c * xy[:, 1]
    turn = np.array([np.cos(-yaw / 2), 0.0, 0.0, np.sin(-yaw / 2)])
    for i in range(len(out)):
        q = np.zeros(4)
        mujoco.mju_mulQuat(q, turn, window[i, 3:7].astype(np.float64))
        out[i, 3:7] = q / np.linalg.norm(q)
    return out.astype(np.float32)


def write(path: str, order: list, clips: dict) -> None:
    def text(s: str) -> bytes:
        b = s.encode("utf-8")
        return struct.pack("<i", len(b)) + b

    parts = [b"PBIC", struct.pack("<ifi", 1, float(FPS), len(order))]
    parts += [text(j) for j in order]
    parts.append(struct.pack("<i", len(clips)))
    for name, rows in clips.items():
        parts += [text(name), struct.pack("<i", len(rows)), np.ascontiguousarray(rows, dtype="<f4").tobytes()]
    with open(path, "wb") as f:
        f.write(b"".join(parts))


def read(path: str):
    """The file back: (fps, joint names, {clip: (frames, 7 + joints) array}). The tests use this too."""
    with open(path, "rb") as f:
        data = f.read()
    assert data[:4] == b"PBIC", "not an intro clip file"
    at = 4
    version, fps, nj = struct.unpack_from("<ifi", data, at)
    at += 12
    assert version == 1, version

    def text():
        nonlocal at
        (n,) = struct.unpack_from("<i", data, at)
        at += 4 + n
        return data[at - n : at].decode("utf-8")

    order = [text() for _ in range(nj)]
    (nc,) = struct.unpack_from("<i", data, at)
    at += 4
    clips = {}
    for _ in range(nc):
        name = text()
        (frames,) = struct.unpack_from("<i", data, at)
        at += 4
        count = frames * (7 + nj)
        clips[name] = np.frombuffer(data, "<f4", count, at).reshape(frames, 7 + nj)
        at += 4 * count
    assert at == len(data), "trailing bytes"
    return fps, order, clips


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--models", default=os.path.join(HERE, "models", "v2"))
    ap.add_argument("--body", default="matt", help="the body the result is checked on")
    ap.add_argument("--survey", action="store_true", help="also print the takes looked at and left out")
    args = ap.parse_args()
    cfg = json.load(open(os.path.join(args.models, f"{args.body}_policy_config.json"), encoding="utf-8"))
    order, lower, upper = cfg["joint_order"], np.array(cfg["lower"]), np.array(cfg["upper"])
    m = mujoco.MjModel.from_xml_path(os.path.join(args.models, f"{args.body}_solo.xml"))
    cache = os.path.join(HERE, "clips", "cmu")
    os.makedirs(cache, exist_ok=True)

    takes = {name: take for name, (take, _) in {**CLIPS, **CELEBRATIONS}.items()}
    if args.survey:
        takes.update({f"({t})": t for t in SURVEY})
    print(
        f"{'clip':14s} {'take':6s} {'s':>5s} {'from':>5s} {'arms':>5s} {'walk':>5s} {'turn':>5s} {'off range':>9s}  thigh shin arm"
    )
    out = {}
    for name, take in takes.items():
        path = os.path.join(cache, take + ".bvh")
        if not os.path.exists(path):
            urllib.request.urlretrieve(URL.format(subject=int(take.split("_")[0]), name=take), path)
        clip, dirs, clipped = retarget_wrapped(path, order, lower, upper)
        celebration = name.startswith(CELEBRATE)
        best = best_window(clip, order, anywhere=celebration)
        if best is None:
            print(
                f"{name:14s} {take:6s} {len(clip) / FPS:5.1f}  no window: too short, or always walking off or turning"
            )
            continue
        s, score, travel, turn = best
        n = int(WINDOW * FPS)
        e = check(m, place(m, clip[s : s + n], order), {k: v[s : s + n] for k, v in dirs.items()}, order)
        e = {p: 0.5 * (e[p + "_l"] + e[p + "_r"]) for p in ("thigh", "shin", "upper_arm")}
        print(
            f"{name:14s} {take:6s} {len(clip) / FPS:5.1f} {s / FPS:5.1f} {score:5.1f} {travel:5.2f} {turn:5.0f} "
            f"{clipped:9.1%}  {e['thigh']:5.1f} {e['shin']:4.1f} {e['upper_arm']:4.1f}"
        )
        # A wrong sign or a wrong order of the hip's three angles shows as tens of degrees here.
        legs_ok = celebration or (e["thigh"] < 3.0 and e["shin"] < 10.0)
        assert legs_ok and e["upper_arm"] < 3.0, f"{name}: the body does not follow"
        if not name.startswith("("):
            out[name] = zeroed(clip[s : s + n])

    missing = [n for n in {**CLIPS, **CELEBRATIONS} if n not in out]
    assert not missing, f"no usable window in {missing}"
    write(OUT, order, out)
    fps, back, clips = read(OUT)
    assert back == order and all(np.array_equal(clips[k], out[k]) for k in out)
    print(f"wrote {OUT}: {len(out)} clips of {WINDOW:.0f} s, {os.path.getsize(OUT) / 1e3:.0f} kB")


if __name__ == "__main__":
    main()
