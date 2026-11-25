"""The repo's own statements about itself, checked instead of trusted.

Three things kept going stale in the "current state" of the docs while this repo was moving fast: the Unity
version, the MuJoCo version (the game's plugin and the trainer's pin must be one number), and who is on the
roster. And one thing was worse than stale: staged training queues that set PYTHONPATH to a second MuJoCo
(Training/.mj350, 3.5.0), which is exactly the version skew the parity rule forbids. All four are cheap to
check, so they are checked here (fast tier, no MuJoCo, no Unity).
"""

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def _read(*parts):
    return (ROOT.joinpath(*parts)).read_text(encoding="utf-8")


def _unity_version():
    match = re.search(r"m_EditorVersion:\s*(\S+)", _read("ProjectSettings", "ProjectVersion.txt"))
    assert match, "ProjectSettings/ProjectVersion.txt has no m_EditorVersion"
    return match.group(1)


def _plugin_version():
    return json.loads(_read("Packages", "org.mujoco", "package.json"))["version"]


def test_docs_state_the_versions_the_project_is_on():
    unity, plugin = _unity_version(), _plugin_version()
    head = "\n".join(_read("docs", "README.md").splitlines()[:12])
    assert f"Unity {unity}" in head, f"docs/README.md's header must name Unity {unity}"
    assert f"plugin {plugin}" in head, f"docs/README.md's header must name the plugin {plugin}"
    assert plugin in _read("AGENTS.md"), f"AGENTS.md must name the plugin version {plugin}"


def test_the_trainer_pins_the_games_mujoco():
    plugin = _plugin_version()
    pyproject = _read("pyproject.toml")
    assert f"mujoco=={plugin}" in pyproject, f"the physics tier must pin mujoco=={plugin} (the plugin's version)"
    assert f"mujoco-warp=={plugin}" in pyproject, f"the training tier must pin mujoco-warp=={plugin}"


def test_every_boxer_with_a_brain_is_named_in_the_docs():
    # The bodies (config, recordings, prefabs) live in Agents/Boxers/NAME; the trained brains in RL_Models/NAME.
    models = ROOT / "Assets" / "_Project" / "RL_Models"
    bodies = ROOT / "Assets" / "_Project" / "Agents" / "Boxers"
    roster = [
        d.name
        for d in sorted(models.iterdir())
        if d.is_dir() and (d / "policy.onnx").exists() and (bodies / d.name).is_dir()
    ]
    assert roster, "no boxer has a policy.onnx under Assets/_Project/RL_Models"
    docs = _read("docs", "README.md")
    missing = [name for name in roster if name not in docs]
    assert not missing, f"docs/README.md does not say who is on the roster: {missing}"


def test_no_staged_queue_pins_a_second_mujoco():
    staging = ROOT / "Training" / "queues"
    offenders = []
    for path in sorted(staging.glob("*.json")):
        text = path.read_text(encoding="utf-8")
        if ".mj350" in text:
            offenders.append(path.name)
    assert not offenders, (
        "these staged queues set PYTHONPATH to Training/.mj350 (MuJoCo 3.5.0); the game, the trainer and "
        f"the exams share one MuJoCo: {offenders}"
    )
