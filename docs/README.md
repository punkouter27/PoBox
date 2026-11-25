# PoBox: running history

Two physics-driven boxers fight in a ring while the game films it like a broadcast: cameras that cut on the
action, a scoreboard, three judges, a referee, a ladder, a crowd and a commentary line. The app starts on a
screen where the two boxers are picked. Portrait, one screen, nothing scrolls. Unity 6000.6.0f1, URP 17.6,
Cinemachine, UI Toolkit. The fight runs on the official MuJoCo Unity plugin 3.5.0, with no PhysX.

This file is the running history, newest first. The overview is the root [README.md](../README.md), the live
plan is [tasks.md](tasks.md), the training log is [rl_optimization_log.md](rl_optimization_log.md), and
the house rules are [AGENTS.md](../AGENTS.md).

## Where it stands (2026-11-13)

- **Eight boxers:** Matt, Zombie, Nick, Trump, Grandma, Grandpa, Kim, Sokol. Each has three brains (footwork,
  boxing, get-up) in `Assets/_Project/Agents/Boxers/NAME`.
- **Six are on the v3 body** (`Training/models/v3`: human force each way, per-joint action scale, de Leva
  masses): Sokol and Nick (2026-11-02), Zombie (2026-11-03), Matt, Trump and Kim (2026-11-04).
- **Grandma and Grandpa are still on v2.** On v3 they got up 5 and 14 times of 60 (2026-11-03). The last log
  entry says more of the same training will not do it; the approach is the owner's call.
- **Known weak lines on v3:** Trump's attack (0.11 punches landed a second, mark 0.5), Kim's footing (about
  2 falls a minute) and carrying on, and the under-pressure line, which nobody passed on 2026-11-03.
- **Crew:** a referee and a ring-card walker are MuJoCo bodies with trained footwork (`Assets/_Project/Agents/Crew`,
  `Training/models/crew`).
- **Last test counts on record (2026-11-04):** MjParity 3/3, MjGate 24/24, StateFlow 2/2, OneScreen 74/74.
- The design documents in [20261004](20261004) describe the game as it was on 4 October (six boxers, v2 bodies).

## Older history

Everything from 30 September to 31 October 2026 is in
[archive/README_history_2026-10.md](archive/README_history_2026-10.md), moved there word for word on
2026-11-13. It also holds the first build's reference sections: "What was built", "Measured", "Running it"
and "Things that cost time, worth knowing". The plan as it stood, with every ticked task, is
[archive/tasks_2026-10.md](archive/tasks_2026-10.md). The October training log is
[archive/rl_optimization_log_2026-10.md](archive/rl_optimization_log_2026-10.md).

## Progressive training: physical limits, a rung ladder, terrain, obstacles (2026-11-25, later)

Twenty more trainer upgrades, the progressive set: what a body physically is, how a skill is grown from the one
below it, and what the rung above a walk asks for. **All of them are off unless their flag is given**: with no new
flag the envs are exactly as they were and every shipped brain stays valid. Unity ML-Agents and UE5
LearningAgents are not in this repo (AGENTS.md: no ML-Agents); every item is in the repo's own PPO
(`Training/ppo.py`) and its MuJoCo Warp envs.

**Physical constraints on the agent** (`Training/envs/limits.py`, `envs/constraints.py`; on `train_box.py`, one
group, applied as the last thing a drive target passes through):

| What | Flag | Notes |
|---|---|---|
| Gravity scaling | `--gravity-scale 0.1` | each world feels g x (1 +- s); the observation's "down" is that g |
| Motor torque caps | `--torque-cap 0.7`, `--torque-var` | the drive's force range, scaled a world, written into `mw.actuator_forcerange` and into the target |
| Actuator latency | `--act-lag 8 --lag-var 0.2` | first-order motor lag, 8 Hz bandwidth (separate from `--domain-rand`'s integer delay) |
| Compliant joint stops | `--soft-limit 0.08 --rom-w 0.01` | inside the last 0.08 rad of a joint's range the target gives way; the press is charged (rt_rom) |
| Range-of-motion readout | (with `--soft-limit`) | `rom_use` in the run's stats |

**Curriculum and hierarchical reuse** (`envs/curriculum.py`, `config/progression.json`, `hierarchy.py`):

| What | Flag | Notes |
|---|---|---|
| The rung ladder: balance -> walk -> sprint -> navigate | `--curriculum auto` (or a rung's name) | promotes/demotes on the run's own stats, with a hold and an EMA; the rung's knobs are written into the live env |
| Sprint rung | `--sprint-share 0.5` | walk episodes that ask for 1.4 m/s up to `cmd_fwd_max`, paid for a stride past 0.35 s |
| Obstacle navigation rung | `--nav-share 0.3 --nav-obstacles 2` | a goal 2-4 m away, cubes parked across the line, the command steers to it (envs/nav.py) |
| A shared encoder | `--shared-encoder` | one trunk behind actor and critic: half the parameters, one body state |
| Freeze the low-level encoder | `--freeze-layers 2 --freeze-until 200` | the new rung learns its head on top of locomotion, then the encoder is let go |
| Reuse a trunk across stages | `--reuse-trunk checkpoints/<run>/latest.pt` | copies the encoder (never the head), shared or separate |
| Mirror data augmentation | `--mirror-aug 0.5` | half of each rollout is trained mirrored, so one step teaches both sides |

**Behavioural expansion** (`envs/terrain.py`, `envs/momentum.py`, `envs/nav.py`, plus the get-up stage that
already exists):

| What | Flag | Notes |
|---|---|---|
| Terrain adaptation | `--terrain` (all classes), `--terrain mild,steep`, `--terrain-bump` | flat/mild/steep floors drawn every episode: slope (tilted gravity), friction, uneven-ground kicks |
| Momentum-aware turning | `--momentum-w 0.1 --turn-accel 12` | the yaw command ramps at 12 rad/s^2; yaw jerk beyond it is charged (rt_mom) |
| Leaning into a turn | `--lean-w 2` | the centripetal lean height x v x w / g is asked for and the miss charged (rt_tilt) |
| Recovery behaviours | `--stage getup` (existing), `nav` goals | the get-up brain is the recovery rung; a navigation episode is a recovery from a shove off line |

Not done in this pass, and deliberately: none of these are mirrored into the game (the game runs the nominal
body, as with `--domain-rand`), the C MuJoCo exams do not model the terrain classes or the motor lag, and no
run has used any of it yet (the GPU had another session's job on it). What was verified: `tests/test_rl_progression.py`
(16 tests: the formulas and the ladder in the fast tier, a full footwork env with every flag on, the shared
encoder with mirroring, freezing and a trunk copied between a shared and a separate policy).

## Twenty upgrades: motion priors, a league, muscles, spray, decals, lip-sync, chat votes (2026-11-25)

Twenty upgrades went in on one day: eight in the trainer, twelve in the game. **Every trainer one is off unless
its flag is given**, so the shipped brains and the parity tests are untouched. In the game, the new pieces are
placed in the scenes and prefabs by one idempotent menu item, `PoBox/Setup/Wire 2026-11 Upgrades`
(`Editor/UpgradeWiring.cs`).

**Trainer** (`Training/`; flags on `train_box.py`, defaults off):

| What | Flag | Files |
|---|---|---|
| Adversarial motion priors: a discriminator over (s, s') judges style instead of clip tracking | `--amp`, `--amp-w 0.5` | `amp.py`, `ppo.py` (`PPO.set_amp`) |
| AMP for the get-up stage, on 6 CMU get-up takes (140_01-04, 08, 09) | `--stage getup --amp` | `amp.py prep-getup [--fetch]` -> `clips/getup.npz` |
| Self-play league: frozen snapshots + shipped brains, PFSP sampling, an exploiter | `--league`, `--league-boxers ...` | `league.py` |
| Domain randomisation: friction, mass, PD gains, actuator delay, dt jitter, sensor noise | `--domain-rand` | `envs/randomize.py`, `config/domain_rand.json` |
| Hill muscle: activation lag, force-length, force-velocity (replaces the linear speed limit) | `--muscle hill` | `envs/muscle.py`; Unity `Mj/HillMuscle.cs`, `Mj/MuscleConfig.cs` |
| Mechanical work penalty, sum of abs(torque x speed) | `--energy-w` (`mech_work` in `reward.json`, 0) | `envs/boxing_base.py` |
| Real hits as training shoves, from the fight log | `--hit-bank PATH` | `tools/hits_to_bank.py`, `envs/hit_bank.py` |
| Teacher-student distillation (privileged teacher, DAgger student on the 103 numbers) | `train_distill.py` | `train_distill.py` |

The Hill muscle runs in Unity only for a boxer whose `config.json` has a `muscle` block (none does yet), so
parity is unchanged; a brain trained with it writes the block and `speed_limit: false` into its policy config.
The C MuJoCo exams do not model the muscle yet: port it there before promoting such a brain.

**Game:**

- **Impact spray** (`Fx/HitSpray.cs`): spit on head punches over 6 m/s, mist off clean body punches, a ring of
  canvas dust at the first heavy landing after a knockdown. Built-in particles (VFX Graph needs compute shaders,
  which WebGL lacks), `Emit` only, no allocations.
- **Bruise swelling and sweat** (`FighterOverlay.shader`, `FighterSkin`): the 8 bruise marks now raise the head's
  overlay a few millimetres; a wet sheen grows with rounds and fatigue.
- **Blood and fall decals** (`CanvasMarks`): 8 blood and 6 scuff projectors that stay for the bout.
- **Splines** (`com.unity.splines` 2.9.2, already pulled in by Cinemachine): the ropes can take a spline rest
  shape and now have UVs; a **dolly camera** on an arc of rail films the intro before the first bell
  (`Fx/BroadcastDolly.cs`, `Shot.Dolly`, `BroadcastDirector.dollyCam`).
- **Towels** over the top rope at the red and blue posts (`Fx/VerletCloth.cs`, CPU Verlet, no PhysX).
- **CC0 materials** from ambientCG (`Environments/Textures/AmbientCG`, credits beside them): braided rope,
  leather pads, canvas fabric, towel.
- **Lip-sync** (`Audio/LipSync.cs` on every corner prefab): the procedural voices write a mouth track; Timmy and
  Zombie move their jaw bone, the others need a mouth transform assigned (no mesh has mouth blendshapes).
- **Modal impact sound** (`ImpactModalSettings`): resonances by contact (head, body, glove, canvas), louder and
  brighter with the measured impulse and glove speed.
- **Reactive crowd** (`CrowdExcitement`): punch rate, big hits, daze and knockdowns drive the crowd's level, a
  roar layer and how far the figures move (`_PoBoxCrowdExcite`).
- **Colour commentary** from Claude (`server/corner_proxy.py` `POST /commentary`, `Commentary.cs`): a round
  summary at each bell, a line after a knockdown at most every 45 s. Needs `ANTHROPIC_API_KEY` on the proxy;
  off or slow (5 s), the canned lines carry on. /diag lists it as Off until the proxy is hosted.
- **Chat votes** (`Online/ChatCorner.cs`, `ChatVote.cs`): between rounds Twitch chat votes `!pressure` /
  `!defend` / ...; the winner changes how the boxer spends energy next round. The brains have no tactics input,
  so the words cannot change which punches are thrown.
- **New boxer in one command**: `python Training/tools/new_boxer.py NAME mesh.glb --bone-map mixamo`
  (rig, MJCF, capsule fit, self-collision check, roster entry).

Also: `pyproject.toml` and `uv.lock`, deleted by accident in `0fdbc09`, are back (CI's `uv sync` needs them).

## Walk-on moves: shadow boxing from motion capture, no training (2026-11-23)

The boxers used to stand still in their corners for the 5 s walk-on. Now each plays 5 s of shadow boxing from
the CMU motion-capture database. Nothing is trained: the walk-on has MuJoCo stopped, so `Sim/IntroMoves.cs`
writes each frame's pose into qpos, runs the kinematics and lets the meshes follow, as the instant replay does.
The clip is turned to the boxer's corner, scaled by its legs, held inside its joint limits, its lower sole kept
on the floor (`MjBoxer.ShowPose`), and eased in from the guard over 0.6 s. When the walk-on ends the bout places
the boxers in their guards on their marks as before.

- **Clips:** `Training/tools/intro_clips.py` (CMU takes 14_01, 14_02, 14_03, 13_17, 15_13, 80_10, 143_23) writes
  `Assets/_Project/Data/Resources/IntroClips.bytes` (7 clips, 196 kB). Each take's 5 s window that moves the
  arms most while the pelvis stays within a step and turns under 50 degrees. Left out: 17_10 (walks off),
  79_94 flexing (leans back past the hips' range), 141_16 wave (2.5 s).
- **Fix found on the way:** `retarget_clips.retarget` unwraps the shoulder angle from the first frame, and a
  boxing guard (upper arm straight ahead) makes it jump a whole turn; the clamp then bent the arm 20-36 degrees
  wrong. `intro_clips.retarget_wrapped` wraps every frame into range and takes the shoulder's other solution
  (x + pi, pi - z) when it fits better. `walk_turn.npz` (training) is untouched: its clips never go there.
- **Signature moves** (owner: yes): every shipped boxer has its own clip (`IntroMoves.Signatures()`, the
  component's starting `byBoxer`), played at the boxer's own `speed` from config.json (Grandma and Grandpa 0.75,
  Kim 1.1), Zombie at 0.7 and Timmy at 1.15 on top. Two corners with the same move start half a clip apart.
- **The winner's celebration** (owner: yes): from the result until the referee raises its hand (at most
  `Referee.raiseAfter` = 6 s), the winner, already held standing where it won (`MjRing.HoldPose`, re-placed every
  step while the loser is simulated), takes the arms and trunk of a `celebrate_*` clip over its gloves-up stance
  each frame (`MjRing.ChangeHeldPose`, `MjBrain.Celebrating`). Clips: chicken dance (20_01), waving (13_26),
  jumping-jack arms (14_06); 10 clips, 280 kB in all.
- **Movens-5M** (owner: yes, looked at, nothing downloaded): the Hugging Face card
  (`wendell0218/Movens-5M`) says only "License: other"; the human part is SMPL-X (whose body model has its
  own non-commercial licence), the robot part is Unitree G1 joints; its labels are mostly dance styles, with no
  boxing or celebration type seen. Needs the licence read and the owner's yes before any of it ships.
- **Tunable** on the `IntroMoves` component (on the Bout's object in Arena): on/off, a clip and speed per boxer
  (`byBoxer`), the pool, blend-in seconds, speed, celebrations on/off and which.
- **Tests:** `tests/test_intro_clips.py` (physics tier: every clip on every shipped body, 11 passed),
  EditMode `IntroClipsTests`, PlayMode `WalkOnMovesTests` (writes `Build/Shots/walkon_*.png` at 1600x900 and
  450x800).
- **Not yet done on this PC (no Unity licence in batchmode, no Unity Hub installed):** adding the component to
  Arena (menu `PoBox/Scene/Add Walk-on Moves To Arena`, or `-executeMethod
  PoBox.EditorTools.SceneExtras.AddIntroMoves`), and running the EditMode/PlayMode tests and screenshots.

## Back to the dressing room; round-2 brains for Timmy and Soldat (2026-11-17)

- **The walk-off.** Eight seconds into the result (`MjBrain.leaveAfter`), after the referee has raised the winner's
  hand, both boxers walk slowly (0.35 m/s, footwork policy) to their own corner, turn, and go out over the side
  beside it. A leaving boxer is on its own collision bit (`MjRing.SetLeaving`, bit 16; 8 is the crew's) that only
  the floor shares, so it passes through the ropes; past the apron edge it is hidden and parked until the next
  bout places it. The referee lets go of the hand when they leave; the broadcast holds a still wide shot on the
  ring centre (`BroadcastDirector.WalkingOff`) instead of framing the gap between the two, which had sent the
  camera into the dark hall. Arena: `resultsSeconds` 26, `cupBackSeconds` 24, so the cup shows the walk.
  The ring is 1.3 m above the hall floor and MuJoCo's floor is a plane at canvas height, so they vanish at the
  apron rather than climb down.
- **Brains:** Timmy 6/7 exam lines (was 3), Soldat 5/7 (was 3), Soldat on his own footwork. Testbed now holds
  Timmy and Soldat, so MjGateTests covers them (30/30).

## Sixteen: the cup, the free-for-all and the voice matrix (2026-11-16)

- **Cup (League/Career.cs):** sixteen slots seeded by the ladder (1 v 16, 8 v 9, ...), BYEs for empty slots; with
  the eight boxers the round of 16 is all walkovers (one "R16 BYES" line on the sheet). A boxer added later takes a
  BYE whose walkover is not used yet (`Career.Fill`). AUTO (on for a new cup): the bracket counts down and fights the
  next bout; the arena goes back to the bracket 10 s after a cup result (`HudView.cupBackSeconds`,
  `MenuView.CupReturning`). Cups won per boxer in PlayerPrefs `pobox.cup.titles`. An old eight-slot `career.json`
  is dropped (NEW CUP).
- **Free-for-all:** Melee.unity holds sixteen: the eight and a twin each ("MATT II", the boxer's Blue prefab, authored
  in the scene), prefixes a_..p_, sixteen colours, a compact board. Placings (winner, the rest by the judges' score,
  then the eliminated, last out first) move the ladder's Elo (`LeagueTable.ReportPlacings`, K 32 shared over pairs; a
  twin counts as its boxer at its better place).
- **Voice matrix (Audio/VoiceMatrix.cs):** boxer x emotion (exhale, effort, pain, groan, pant, dazed, taunt, triumph,
  defeat), from the first tier that has it: recorded clips `A_Voice_<Boxer>_<Emotion>_<n>` under any
  `Resources/Voices` folder; the shared low/high takes (half the picks, five emotions); a procedural source-filter
  voice per boxer (`VoiceSynth`, `VoiceProfile`). New calls: a dazed moan, a laugh after a knockdown scored, a roar for
  the winner, a sigh for the loser and for each boxer counted out of a free-for-all.
- **Measured, 16 in the melee (editor, desktop):** a brain call is 0.28-0.36 ms, almost all Inference Engine overhead
  (a batch of 16 on one network costs 0.33 ms); 16 brains are 5.5-8 ms per 20 ms control step. Batching helps only
  twins on the same brain (14 distinct networks of 16 in a sample).
- **Direct brains (Mj/MlpPolicy.cs):** every brain's graph (Sub, Div, Clip, Dense, Elu) is read once into a flat
  buffer and run as one Burst job on the calling thread, safety checks off, `[NoAlias]` Dense. Plain C# was 1.3 ms, the
  Burst job 0.048 ms against the Inference Engine's 0.35 ms; in the live 16-boxer melee the brains went from about 8
  to 2.8 ms a control step. `MjBoxer.DirectBrains` (on) switches it; a graph it cannot run stays on the Inference
  Engine with a warning. `MlpPolicyTests` 2/2 (every shipped brain within 1e-4 of the Inference Engine, and faster),
  MjParityTests 3/3, MjGateTests 24/24.
- Tests: `CupTests` 7/7 (cup, fill, placings Elo, procedural voices, clip names). Not yet: the UI screenshot report,
  a listen to the procedural voices, the 16-boxer frame rate on a phone.

## Timmy and Soldat, the referee's raise, solid falls, a 16-slot menu (2026-11-16, evening)

- **Timmy (9th boxer)**: the owner's AccuRig FBX staged in `BLENDER/2026-11-16_timmy/stage.py` (its clips posed it into
  spikes; the mesh did not sit on its skeleton and was fitted onto it by the skin weights; legs straightened from 27
  degrees apart; 1.76 m, 73 kg). Persona **infighter**: range 0.56 m, body 2.0, lead hand 1.3. Warm start Sokol.
- **Soldat (10th)**: an unrigged mesh, rigged in `BLENDER/2026-11-16_soldat/rig.py` (joints from the mesh's own slices,
  Mixamo names, heat weights; 1.84 m, 87 kg). Persona **marksman**: the rear hand 1.6 to the head from 0.84 m. Warm start Matt.
- Both: v3 bodies, 0 differences Unity against the trainer, corners in Arena, a slot each in the free-for-all (in place of
  GRANDMA II and GRANDPA II), menu cards, ladder entrants, their own procedural voices. **Training is still running**
  (`Training/logs/queue_timmy.json`); until their brains are promoted they run the zero placeholder and fall if picked.
  Timmy's footwork exam: stand, turn and joints pass, walking falls 0.203 a minute (mark 0.2).
- **Referee raises the winner's hand**: at the result he stands at the winner's left facing the camera the result came up
  on and lifts his right arm with the winner's left (`MjBoxer.RaisedLeft`, `MjBrain.HandRaisedByReferee`); within a metre of
  his spot he is set on it and held (`MjCrew.PinPose`: his walking brain cannot balance an arm overhead). Measured: hands
  1.74 and 1.90 m up, 0.19 m apart.
- **Falls are solid in a pair's bout** (`MjRing.solidDowned`): a boxer down is no longer passed through; the free-for-all's
  layers, the one held over a count kept on the standing layer. Pass-through overlap 18 cm -> 0 over several knockdowns.
- **Menu**: a 4 x 4 grid of 16 slots, EMPTY cards for the unfilled ones (`MenuView.Grid`).
- Tests: MjGate 24/24, StateFlow 2/2. **FreeForAllTests fails** (2 of 4 boxers land a punch in its first 20 s) while
  training saturates the GPU; to re-run when it has finished.

## Play-through fixes: twins that could not be hurt, the 16-boxer pile-up (2026-11-16, later)

- **Twins took no punch damage.** Their MjBrain had no `ring` (a scene override the Red instances had), so the ring's
  punches never reached them; they took the top seven places of a melee on 0 punches landed. FreeForAll now wires
  every boxer's brain and `ringHalf` before switching it on; FreeForAllTests checks it. The ladder moves of that
  melee were reversed exactly. The scene's `colours` held 8 (twins wore their boxer's): 16 now.
- **Pile-up:** sixteen in the six-metre ring lost fourteen inside half a round. Now `maxInRing` 8: the rest wait off
  stage (held, hidden, brains skipped: `MjRing.SetIdle`) and tag in, one per boxer counted out and one every 12 s
  while fewer than 10 are in; the counted out are carried off at the break; nobody wins while anybody waits; those
  who never got in are not rated. Board rows show NEXT.
- Smaller: the compact board's text spilled (rows 66 px); the call sat on the fourth row (under-board variables
  moved for sixteen); the cup's byes line ran off ("R16 8 BYES · ALL THROUGH"); AUTO showed OFF before any cup;
  ReportPlacings would have dropped every record after a CPU-ratings change (it now keeps them, as Load does);
  every procedural voice is made before the bell; all three brains load at Awake (the first knockdown loaded the
  get-up brain mid-fight); the HUD debug key MODE/SEED was clipped on every phone (OneScreenTests red since cf8c28a).
- Open: the grandparents still go down from light punches and cannot get up (V1, a training question).

## Overnight 2: the referee and card walker walk on their own, Matt punches (2026-11-04)

- Report `docs/reports/2026-11-04-overnight.html`; best weights `docs/reports/2026-11-04-overnight-weights.json`.
- **Crew:** referee and ring-card walker built from the stylised figures (`Training/tools/build_crew.py`, models/crew),
  footwork trained from Sokol's and Nick's (`crewf1_*`), the referee again with his arm held (`--gesture`, `crewf2`).
  Every C MuJoCo footwork line passes; both in the game (Assets/_Project/Agents/Crew, Mj/MjCrew), 0 differences from the trainer.
  Game-side: the crew changes episode only from standing (MjCrew.GoTo); a fallen referee is stood back up at a quiet moment.
- **Matt** `v3p2_matt` + `v3u5_matt`: all six original lines pass (attack 0.18 -> 0.66/s); fails only the new
  under-pressure line, on Zombie. Not promoted (owner's call).
- **Trump, Kim:** punching lessons did not take (with a fresh critic too, `train_gauntlet.py --reset-critic`); kept as
  they were. **Grandma, Grandpa:** not trained; their get-up needs a design decision.
- **Morning lessons (2026-11-04):** Matt promoted (v3f/v3p2/v3u5). Trump closing distance (`v3c1_trump`, press 1.2,
  range 0.5): attack 0.05 -> 0.11/s, guard passes again; still short of 0.5, not promoted. Kim footing (`v3f2_kim`, fall
  penalty 30): worse (attack 0.07/s, 2.0 falls a minute); stays on v3m3.
- **Trump promoted (owner: "promote brains")** onto the v3 body: footwork v3f_trump, boxing v3c1_trump, get-up v3u4_trump.
  0 differences; MjParity 3/3, MjGate 24/24, StateFlow 2/2, OneScreen 74/74. Kim then moved too (owner's call): footwork v3f_kim, boxing
  v3m3_kim, get-up v3u3_kim; 0 differences. Her boxing still falls about twice a minute in the exam.

## Overnight training: Nick passes everything, Matt gets up (2026-11-03)

- About 9 h of lessons in urgency order (owner: "EXTENDED TRAINING (10 hours)"). Report with charts:
  `docs/reports/2026-11-03-overnight.html`; best brains per boxer: `docs/reports/2026-11-03-overnight-weights.json`.
- **Nick** `v3m9_nick` (reward for staying up doubled, get-ups facing any way): carrying on 97% under the game's rule
  (was 84%): all six lines pass. Ready to replace v3m8 in the game.
- **Zombie** passes all six lines under the game's get-up rule with his existing v3 brains; moved to v3 in the working
  tree (parity 0 differences) but not committed: the Unity tests were blocked by a save-scene dialog.
- **Matt** `v3u5_matt` get-up: 54/60 (was 31/60), carrying on 53/54. Attack still fails, so he stays on v2.
- **Grandma** 5/60 and **Grandpa** 14/60 get up (both were 0/60): a start, far below the mark. **Trump**'s attack lesson
  `v3m6_trump` made him fall more (the critic diverged) and was stopped; v3m5 stays.
- Game side, uncommitted: `Bout.refereeCheckSeconds` (2 s): after a get-up the opponent stays held while the referee looks
  the boxer over, no punch scores, a fall continues the count. Waits for the Unity tests with Zombie.
- **Morning (2026-11-03, Unity open):** Nick promoted with `v3m9_nick` boxing (only `match.onnx` changes; the body and
  get-up are v3u4 as before). `MjParityTests` 3/3, `MjGateTests` 24/24, `StateFlowTests` 2/2. Referee check committed.
  Bouts: Nick beat Zombie by TKO in R1 (67 thrown, 52 landed, 3 knockdowns); 0 errors. **Zombie's v3 move held back:**
  in the game the v3m4 brain hardly punches: 3 thrown against Nick, 2 in three rounds against Sokol, 39 in three rounds
  against Matt, where the v2 brain threw 110-180 against Matt and stopped him. The exam's attack line (0.65/s) does
  not see it; it is a passive brain under pressure, not a parity fault (parity 0 differences). Zombie stays on v2
  until his attack is trained against the strong boxers.
- **Zombie punching lessons (2026-11-03, 12:10-13:28, owner: "ok. you have 60 minutes max", then "another 60 minutes").**
  New exam line `under pressure` (`tools/exam.py`): punches landed a second against the boxer's *hardest* opponent,
  mark 0.25 (the attack line's mean hid Zombie hitting only the weak ones). v3m4 failed it: 0.02/s against Kim.
  `v3m5_zombie` (from v3m4; hit_w 2.2, taken_w 0.7, block_w 0.25, weights nick/sokol/kim/trump=2, 0.6 h): Nick
  0.11 -> 1.5/s in the exam, but value loss rose 10 -> 200 after iteration 150 (the v3m6_trump pattern). `v3m6_zombie`
  (from v3m5 model_000200, weights kim=4 matt=3 sokol=3, `--lr-max 6e-5`, 0.6 h): value loss steady. Exam
  (`exam_v3q_zombie`, rounds 2, game-rule get-up): attack 1.14/s (was 0.71), Kim 1.42, Nick 1.22, Sokol 0.20, Matt 0.02
  (the exam's v3 Matt does not engage: no blocks, no hits either way); getting up 60/60, carrying on 58/60; under
  pressure still FAIL (Matt). **In the game:** against Sokol 92 thrown, 73 landed (was 2/0), against Nick a TKO win in
  R1 (3 knockdowns). Parity 3/3, gate 24/24. **Zombie promoted on v3** (v3f footwork, v3m6 boxing, v3u4 get-up).
  Report: `docs/reports/2026-11-03-zombie-punching.html`.

## Sokol on the v3 body, the first boxer through the exam's six lines (2026-11-02)

- **Sokol is promoted on models/v3** (owner: "3", promote and tighten training): footwork `v3f_sokol`, boxing
  `v3m6_sokol` (the new `--recover-w` reward), get-up `v3u4_sokol`. He passes footing, attack, guard, chin and getting up
  in the C exam; carrying on is 53/57 (93%) under the game's get-up rule (`exam_v2.py --getup-no-drain`, reserve 0.5,
  nothing drained while rising) and 47/54 (87%) under the exam's stricter drained rule. The other seven stay on v2.
- `MjRetrofit` builds a boxer from `models/v3` / `rigs/v3` when it is in `OnV3` (just `sokol`); everything else as before.
  `BuildCorners` no longer writes the old daze numbers (2.5/14/42/0.3) over Fighter's own (3.5/9/26/0.45, review fix a8cd70d);
  a rebuild had been undoing that fix.
- The v3 fingerprints were stale (written before the 10-30 de Leva/trunk rebuild): regenerated under MuJoCo 3.5.0.
  Unity's Sokol then compiles to the trainer's model with 0 differences. New C MuJoCo recordings for the gate tests
  (reference trajectory, 25 fall episodes: 0% down, the guard held). MjParityTests 3/3, MjGateTests 24/24.
- In the game: Sokol v Nick, unanimous decision 30-27 x3 (160 landed to 80, no knockdowns); a forced knockdown
  (`down 0`): up within 10 s and still up 6 s later. The free-for-all ran with no errors.
- **Nick joins him on v3** the same afternoon (owner's word, at five of six lines; carrying on 84% under the game's rule):
  `v3f_nick`, `v3m8_nick` (a lesson whose get-up starts face any way: `--handover-yaw`), `v3u4_nick`. Parity 0 differences,
  gate tests 24/24. Nick beat Sokol by KO in round 2; forced down once, he got up but fell twice more inside 25 s.

