# PoBox

A boxing broadcast for phones. Two physics-driven boxers fight in a ring while the game films it with
cameras that cut on the action, scores it with three judges, and commentates. Nobody animates the boxers:
each is a body with 21 joints and a trained brain that decides, 50 times a second, where every joint goes.

Portrait, one screen, nothing scrolls. Unity 6000.6 (URP, Cinemachine, UI Toolkit), the official MuJoCo
Unity plugin 3.5.0 for the fight's physics, and Unity Inference Engine for the brains. No PhysX in the fight.

## What is in it

- **Eight boxers**: Matt, Zombie, Nick, Trump, Grandma, Grandpa, Kim and Sokol, each built from its own
  rigged mesh, with mass and strength to match its size.
- **Three trained skills each**: footwork (stand under shoves, walk, turn), getting up from the canvas,
  and boxing. All trained in MuJoCo Warp on the GPU (`training/`).
- **A bout**: a walk-on, three rounds of 45 seconds, stools between rounds, knockdowns with a count of ten,
  a referee who looks a boxer over after a get-up, and a knockout or a judges' decision. A ladder keeps Elo
  ratings on the device.
- **A crew**: the referee and the ring-card walker are MuJoCo bodies too, walking on trained footwork.
- **A free-for-all**: all eight in the ring at once (menu ALL).
- **A broadcast**: cameras, a scoreboard, commentary, a crowd, replays and a ring announcer.

## Where the boxers stand (13 November 2026)

Six boxers are on the v3 body (`training/models/v3`). Grandma and Grandpa are still on v2: on v3 they got up
5 and 14 times of 60 (2026-11-03), far below the mark.

The exam runs in plain C MuJoCo 3.5.0 (`training/tools/exam.py`). It has six original lines (footing, attack,
guard, chin, getting up, carrying on) and, since 2026-11-03, a seventh: under pressure, punches landed
against the boxer's hardest opponent. Figures below are the last ones in
[rl_optimization_log.md](rl_optimization_log.md), with their dates. Get-up figures use the game's rule.

| Boxer | Body | Brains (footwork, boxing, get-up) | Last exam | Open |
|---|---|---|---|---|
| Nick | v3 | v3f, v3m9, v3u4 | All six lines pass: attack 1.68/s, getting up 58/60, carrying on 56/58 (2026-11-03) | Under pressure |
| Matt | v3 | v3f, v3p2, v3u5 | All six pass: attack 0.66/s, getting up 60/60, carrying on 59/60 (2026-11-04) | Under pressure: 0.02/s against Zombie |
| Zombie | v3 | v3f, v3m6, v3u4 | Attack 1.14/s, getting up 60/60, carrying on 58/60 (2026-11-03) | Under pressure: 0.02/s against Matt |
| Sokol | v3 | v3f, v3m6, v3u4 | Footing, attack 0.58/s, guard, chin and getting up pass; carrying on 53/57 (2026-11-02) | Under pressure |
| Trump | v3 | v3f, v3c1, v3u4 | Footing and guard pass, getting up 59/60, carrying on 55/59 (2026-11-04) | Attack: 0.11/s, mark 0.5 |
| Kim | v3 | v3f, v3m3, v3u3 | Getting up 60/60 (2026-11-02) | Footing: about 2 falls a minute. Attack: 0.39/s (2026-11-03). Carrying on: 49/60 |
| Grandma | v2 | October brains | Not re-examined in November. October figures are in the [archived log](DOCS/archive/rl_optimization_log_2026-10.md) | Getting up on v3: 5/60 |
| Grandpa | v2 | October brains | As Grandma | Getting up on v3: 14/60 |

Nobody passed the under-pressure line when all eight were examined on 2026-11-03. Every boxer in the game
compiles to the trainer's model with no difference, and the Unity gate tests passed 24 of 24 on 2026-11-04.

## Run it

1. Open the project in Unity 6000.6.0f1.
2. Open `Assets/Scenes/Menu.unity` and press Play. Pick a boxer for each corner, then FIGHT. ALL starts the
   free-for-all.
3. `Assets/Scenes/Arena.unity` goes straight to the default pair. `Assets/Scenes/Melee.unity` is the
   free-for-all. `Assets/Scenes/Testbed.unity` checks one boxer alone with shoves and thrown cubes.

Android: menu `PoBox/Android/Prepare`, then `PoBox/Android/Build APK` (arm64, Android 12 or later).

WebGL: a push to `master` builds and deploys it from the dev PC (`.github/workflows/webgl.yml`,
`Tools/ci_webgl.ps1`).

Tests: play-mode fixtures in `Assets/Scripts/Tests` (`MjParityTests`, `MjGateTests`, `MjCollisionTests`,
`OneScreenTests`, `StateFlowTests`, `SceneIntegrityTests`, `FreeForAllTests`, `FeatureTests`). How to run
them is in [CLAUDE.md](CLAUDE.md).

## Documentation

- [DOCS/README.md](DOCS/README.md): where the project stands and the running history, newest first.
- [tasks.md](tasks.md): the live plan, open items only.
- [rl_optimization_log.md](rl_optimization_log.md): the training log, run by run, November onward.
- [AGENTS.md](AGENTS.md) and [CLAUDE.md](CLAUDE.md): house rules and working notes for AI agents.
- [training/README.md](training/README.md): the trainer.
- [DOCS/archive](DOCS/archive): the October history, plan and training log, moved word for word.
- [DOCS/reports](DOCS/reports): dated training and UI reports.

The design set in [DOCS/20261004](DOCS/20261004) is a snapshot of 4 October 2026: six boxers on the v2
bodies, before the free-for-all, the crew and v3. The architecture still holds; the boxer figures do not.

| Document | What it covers |
|---|---|
| [architecture_overview.md](DOCS/20261004/architecture_overview.md) | The decision loop, what a boxer senses and controls, scoring, tuning settings |
| [architecture_dashboard.html](DOCS/20261004/architecture_dashboard.html) | Component map, step-by-step sequence diagrams, data model |
| [creatures_dashboard.html](DOCS/20261004/creatures_dashboard.html) | Scorecards per boxer, with an Executive and a Technical view |
| [abilities_and_training.html](DOCS/20261004/abilities_and_training.html) | The abilities grid and three annotated TensorBoard charts |
| [model_summary.md](DOCS/20261004/model_summary.md) | Every brain file and every body |
| [scene_layout.html](DOCS/20261004/scene_layout.html) | The ring and testbed to scale, spawn points, engine setup |
| [creature_benchmarks.html](DOCS/20261004/creature_benchmarks.html) | Learning speed, energy and smoothness, running cost |
| [training_metrics_guide.md](DOCS/20261004/training_metrics_guide.md) | The training charts in plain English |

## Folder map

```
Assets/Scenes        Menu, Arena, Melee, Testbed
Assets/Boxers/NAME   one boxer: prefabs, config.json, three ONNX brains, test recordings
Assets/Crew          the referee and the ring-card walker
Assets/Scripts/Mj    the fight's physics loop on the MuJoCo plugin (MjRing, MjBoxer, MjCrew)
Assets/Scripts/Runtime  bout rules, judges, cameras, commentary, HUD, sound, effects
Assets/Scripts/Editor   importer (MjRetrofit), scene and asset tools, Android and WebGL builds
Assets/Scripts/Tests    play-mode tests, including the training-to-Unity gate
Packages/org.mujoco, Packages/bin.mujoco   the MuJoCo plugin and its libraries (embedded)
training/            the trainer: environments, PPO, exams, body models (v2, v3, crew)
server/              the cornerman's key-holding proxy
BLENDER/             Blender sources
DOCS/                history, archive and dated reports
SCREENSHOTS/         before/after reports of UI and graphics passes
```
