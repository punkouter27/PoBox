# PoBox

A boxing broadcast for phones. Two physics-driven boxers fight in a ring while the game films it with
cameras that cut on the action, scores it with three judges, and commentates. Nobody animates the boxers:
each is a body with 21 joints and a trained brain that decides, 50 times a second, where every joint goes.

One screen, nothing scrolls, portrait or landscape. Unity 6000.6.9f1 (URP, Cinemachine, UI Toolkit), the official MuJoCo
Unity plugin 3.15.0 for the fight's physics, and Unity Inference Engine for the brains. No PhysX in the fight.

## What is in it

- **Eight boxers**: Matt, Zombie, Nick, Trump, Grandma, Grandpa, Kim and Sokol (Soldat and Timmy joined on
  2026-11-16; the roster has sixteen slots), each built from its own
  rigged mesh, with mass and strength to match its size.
- **Three trained skills each**: footwork (stand under shoves, walk, turn), getting up from the canvas,
  and boxing. All trained in MuJoCo Warp on the GPU (`Training/`).
- **A bout**: a walk-on, three rounds of 45 seconds, stools between rounds, knockdowns with a count of ten,
  a referee who looks a boxer over after a get-up, and a knockout or a judges' decision. A ladder keeps Elo
  ratings on the device.
- **A crew**: the referee and the ring-card walker are MuJoCo bodies too, walking on trained footwork.
- **A free-for-all**: all eight in the ring at once (menu ALL).
- **A broadcast**: cameras, a scoreboard, commentary, a crowd, replays and a ring announcer.

## Tournament standard (shared across the Po games, 2026-11-22)

- **BYE**: a slot with nobody in it says BYE, on the first screen's 4x4 grid (`Runtime/UI/MenuView.cs`) and in
  the cup's bracket.
- **The cup** (`Runtime/League/Career.cs`, kept in `persistentDataPath/career8.json`): 8 slots, single elimination,
  seeded by rating in the order 1v8, 4v5, 2v7, 3v6. Old 16-slot `career.json` saves are left untouched but cannot
  be resumed in the new cup. BYEs take the bottom seeds; a BYE's
  opponent goes through unfought; BYE v BYE sends a BYE on; a draw goes to the higher seed; it runs itself bout to bout.
- **Elo** (`Runtime/League/LeagueTable.cs`, kept in PlayerPrefs): every boxer starts at 1000, K 32 (reset to 1000
  on 2026-11-22: ratings no longer start from the CPU league in `Assets/_Project/Data/Resources/cpu_elo.json`, which
  is now only shown as each boxer's CPU record on the ladder). Ratings already saved on a device are kept; a fresh
  profile, a new boxer or the settings' reset starts at 1000.
- **Free-for-all** (`Runtime/Sim/FreeForAll.cs`, `Melee.unity`, menu ALL): exactly 8 players (`FreeForAll.Players`).
  The scene's own boxers are seated first; if there are fewer than eight, twins ("MATT II") fill the open seats.
- **Demo matches are rated** (`Runtime/League/RatedMatch.cs`): every completed bout, demo/attract ones included,
  moves the same ladder (draw = half a win each, no seed rule); a free-for-all is pairwise by placing with K 32 shared
  over the N - 1 opponents; a twin is its boxer (never rated against itself, counted once at its better placing).
  A bout left, restarted or abandoned records nothing, and none counts twice. The FFA board shows each Elo and change.

### Voice matrix: 5 emotions x 5 levels

Every boxer has a 5x5 voice grid: `AudioDirector.Play(fighter, Emotion5, level, volume)` (`Runtime/Audio/`,
`VoiceMatrix.Pick(boxer, Emotion5, level, voice)`), level 1 (barely) to 5 (all out). A twin ("MATT II") uses its
boxer's voice.

**The manifest**: `Assets/_Project/Data/Resources/Voices/voices.json` (schema `po-voice-manifest/1`) lists every
roster boxer (each folder under `Assets/_Project/Agents/Boxers` with a `config.json`) x 5 emotions x 5 levels:
the WAV each cell uses (`<id>/<emotion>-<level>.wav` under the Voices folder, e.g. `matt/hurt-3.wav`), whether it
`exists`, and the boxer's procedural `voice` (pitchHz, formantScale, breath, rasp, vibratoHz, vibratoDepth, tempo,
tremor, growl, timbre). Edit a `voice` there to change how a boxer sounds; never edit `exists` by hand.

**Adding a WAV**: drop it in at the path the manifest lists (e.g. `Resources/Voices/matt/hurt-3.wav`). Importing
it reruns the generator; menu **PoBox/Voices/Rebuild voices.json** does the same and logs "N of M clips recorded;
missing: ...". The generator keeps the voices already in the file and adds new roster boxers.

**What plays**, per cell: the WAV if the manifest says it exists; otherwise the boxer's own recording of the older
emotion the cell maps to (`A_Voice_<Boxer>_<Emotion>_<n>`, if there is one); otherwise the procedural imitation
(`VoiceSynth.Emotion5.cs`): a Rosenberg glottal pulse with jitter, shimmer and a subharmonic, aspiration noise,
four gliding formants, 1-4 syllables with consonants, per-emotion prosody, in the boxer's manifest voice;
deterministic per (boxer, emotion, level, take), made at the walk-on and cached. A missing manifest makes every
cell procedural (one warning).

| Emotion5 | Imitation (old emotion if recorded) | Heard at |
|---|---|---|
| angry | "hup!" / "rraah(-hah)" (Effort) | a heavy punch thrown (hook/uppercut 3-5, body 2-3) |
| happy | "heh", then laughs (Triumph) | a drawn bout (level 2) |
| hurt | "oof", gasp + "ugh", gasp + long cry at 5 (Pain 1-3, Groan 4-5) | a clean hit (level from its impulse), a knockdown (5), groans down or spent (4) |
| insult | "pfft-heh" / "nyah-nyah" (Taunt) | a far harder punch than usual (2-3), a knockdown scored (4) |
| celebrate | "yeah!" / "woo-hoo!" + laugh (Triumph) | a win (5 by stoppage or last one standing, 4 on the cards) |

Level 1 to 5 raises loudness, length, pitch range, rasp, syllable count and vibrato. Breath, panting, the dazed
moan and the loser's sigh stay as they were (Exhale, Pant, Dazed, Defeat, with the older synth).

## Where the boxers stand (13 November 2026)

Six boxers are on the v3 body (`Training/models/v3`). Grandma and Grandpa are still on v2: on v3 they got up
5 and 14 times of 60 (2026-11-03), far below the mark.

The exam runs in plain C MuJoCo (`Training/tools/exam.py`; 3.5.0 until 2026-11-20, now the game's 3.15.0, which replays every 3.5.0 recording to within 1e-5 rad). It has six original lines (footing, attack,
guard, chin, getting up, carrying on) and, since 2026-11-03, a seventh: under pressure, punches landed
against the boxer's hardest opponent. Figures below are the last ones in
[docs/rl_optimization_log.md](docs/rl_optimization_log.md), with their dates. Get-up figures use the game's rule.

| Boxer | Body | Brains (footwork, boxing, get-up) | Last exam | Open |
|---|---|---|---|---|
| Nick | v3 | v3f, v3m9, v3u4 | All six lines pass: attack 1.68/s, getting up 58/60, carrying on 56/58 (2026-11-03) | Under pressure |
| Matt | v3 | v3f, v3p2, v3u5 | All six pass: attack 0.66/s, getting up 60/60, carrying on 59/60 (2026-11-04) | Under pressure: 0.02/s against Zombie |
| Zombie | v3 | v3f, v3m6, v3u4 | Attack 1.14/s, getting up 60/60, carrying on 58/60 (2026-11-03) | Under pressure: 0.02/s against Matt |
| Sokol | v3 | v3f, v3m6, v3u4 | Footing, attack 0.58/s, guard, chin and getting up pass; carrying on 53/57 (2026-11-02) | Under pressure |
| Trump | v3 | v3f, v3c1, v3u4 | Footing and guard pass, getting up 59/60, carrying on 55/59 (2026-11-04) | Attack: 0.11/s, mark 0.5 |
| Kim | v3 | v3f, v3m3, v3u3 | Getting up 60/60 (2026-11-02) | Footing: about 2 falls a minute. Attack: 0.39/s (2026-11-03). Carrying on: 49/60 |
| Grandma | v2 | October brains | Not re-examined in November. October figures are in the [archived log](docs/archive/rl_optimization_log_2026-10.md) | Getting up on v3: 5/60 |
| Grandpa | v2 | October brains | As Grandma | Getting up on v3: 14/60 |

Nobody passed the under-pressure line when all eight were examined on 2026-11-03. Every boxer in the game
compiles to the trainer's model with no difference, and the Unity gate tests passed 24 of 24 on 2026-11-04.

## Run it

1. Open the project in Unity 6000.6.9f1.
2. Open `Assets/_Project/Scenes/Menu.unity` and press Play. Pick a boxer for each corner, then FIGHT. ALL starts the
   free-for-all.
3. `Assets/_Project/Scenes/Arena.unity` goes straight to the default pair. `Assets/_Project/Scenes/Melee.unity` is the
   free-for-all. `Assets/_Project/Scenes/Testbed.unity` checks one boxer alone with shoves and thrown cubes.

Android: menu `PoBox/Android/Prepare`, then `PoBox/Android/Build APK` (arm64, Android 12 or later).

WebGL (live at https://gentle-mushroom-0d162090f.6.azurestaticapps.net, status at `/diag`): `scripts/publish_webgl.ps1`
runs the EditMode tests, builds the player on this PC and publishes a `webgl-*` release, which
`.github/workflows/webgl.yml` deploys. In the browser, `Tab` (or the corner button on a touch screen) shows FPS,
the bout's reward, inference latency and the curriculum stage; after 30 s without input the game runs a demo.

Tests: Unity fixtures in `Assets/_Project/Core/Scripts/Tests` (`MjParityTests`, `MjGateTests`, `MjCollisionTests`,
`OneScreenTests`, `StateFlowTests`, `SceneIntegrityTests`, `FreeForAllTests`, `FeatureTests`, ...) and Python in
`tests/` (`uv run pytest`; tiers `-m physics`, `-m train`). How to run them is in [AGENTS.md](AGENTS.md).

## Documentation

- [docs/README.md](docs/README.md): where the project stands and the running history, newest first.
- [docs/tasks.md](docs/tasks.md): the live plan, open items only.
- [docs/rl_optimization_log.md](docs/rl_optimization_log.md): the training log, run by run, November onward.
- [AGENTS.md](AGENTS.md): the Po game standard, house rules and working notes (CLAUDE.md points there).
- [Training/README.md](Training/README.md): the trainer.
- [docs/archive](docs/archive): the October history, plan and training log, moved word for word.
- [docs/reports](docs/reports): dated training and UI reports.

The design set in [docs/20261004](docs/20261004) is a snapshot of 4 October 2026: six boxers on the v2
bodies, before the free-for-all, the crew and v3. The architecture still holds; the boxer figures do not.

| Document | What it covers |
|---|---|
| [architecture_overview.md](docs/20261004/architecture_overview.md) | The decision loop, what a boxer senses and controls, scoring, tuning settings |
| [architecture_dashboard.html](docs/20261004/architecture_dashboard.html) | Component map, step-by-step sequence diagrams, data model |
| [creatures_dashboard.html](docs/20261004/creatures_dashboard.html) | Scorecards per boxer, with an Executive and a Technical view |
| [abilities_and_training.html](docs/20261004/abilities_and_training.html) | The abilities grid and three annotated TensorBoard charts |
| [model_summary.md](docs/20261004/model_summary.md) | Every brain file and every body |
| [scene_layout.html](docs/20261004/scene_layout.html) | The ring and testbed to scale, spawn points, engine setup |
| [creature_benchmarks.html](docs/20261004/creature_benchmarks.html) | Learning speed, energy and smoothness, running cost |
| [training_metrics_guide.md](docs/20261004/training_metrics_guide.md) | The training charts in plain English |

## Folder map

```
Assets/_Project/Scenes        Menu, Arena, Melee, Testbed
Assets/_Project/Agents/Boxers/NAME   one boxer: prefabs, config.json, three ONNX brains, test recordings
Assets/_Project/Agents/Crew          the referee and the ring-card walker
Assets/_Project/Core/Scripts/Mj    the fight's physics loop on the MuJoCo plugin (MjRing, MjBoxer, MjCrew)
Assets/_Project/Core/Scripts/Runtime  bout rules, judges, cameras, commentary, HUD, sound, effects
Assets/_Project/Core/Scripts/Editor   importer (MjRetrofit), scene and asset tools, Android and WebGL builds
Assets/_Project/Core/Scripts/Tests    play-mode tests, including the training-to-Unity gate
Packages/org.mujoco  the MuJoCo plugin 3.15.0 (embedded; native libs also in Assets/Plugins/MuJoCo, Assets/Plugins/WebGL/MuJoCo)
Training/            the trainer: environments, PPO, exams, body models (v2, v3, crew)
server/              the cornerman's key-holding proxy
scripts/             WebGL publish/deploy, play-mode test runner, screenshots.py (before/after reports)
tests/               Python tests (pytest)
BLENDER/             Blender sources
docs/                history, plan, training log, archive and dated reports
SCREENSHOTS/         before/after reports of UI and graphics passes (scripts/screenshots.py writes one per change)
```
