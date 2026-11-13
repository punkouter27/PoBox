# PoBox: running history

Two physics-driven boxers fight in a ring while the game films it like a broadcast: cameras that cut on the
action, a scoreboard, three judges, a referee, a ladder, a crowd and a commentary line. The app starts on a
screen where the two boxers are picked. Portrait, one screen, nothing scrolls. Unity 6000.6.0f1, URP 17.6,
Cinemachine, UI Toolkit. The fight runs on the official MuJoCo Unity plugin 3.5.0, with no PhysX.

This file is the running history, newest first. The overview is the root [README.md](../README.md), the live
plan is [tasks.md](../tasks.md), the training log is [rl_optimization_log.md](../rl_optimization_log.md), and
the house rules are [AGENTS.md](../AGENTS.md).

## Where it stands (2026-11-13)

- **Eight boxers:** Matt, Zombie, Nick, Trump, Grandma, Grandpa, Kim, Sokol. Each has three brains (footwork,
  boxing, get-up) in `Assets/Boxers/NAME`.
- **Six are on the v3 body** (`training/models/v3`: human force each way, per-joint action scale, de Leva
  masses): Sokol and Nick (2026-11-02), Zombie (2026-11-03), Matt, Trump and Kim (2026-11-04).
- **Grandma and Grandpa are still on v2.** On v3 they got up 5 and 14 times of 60 (2026-11-03). The last log
  entry says more of the same training will not do it; the approach is the owner's call.
- **Known weak lines on v3:** Trump's attack (0.11 punches landed a second, mark 0.5), Kim's footing (about
  2 falls a minute) and carrying on, and the under-pressure line, which nobody passed on 2026-11-03.
- **Crew:** a referee and a ring-card walker are MuJoCo bodies with trained footwork (`Assets/Crew`,
  `training/models/crew`).
- **Last test counts on record (2026-11-04):** MjParity 3/3, MjGate 24/24, StateFlow 2/2, OneScreen 74/74.
- The design documents in [20261004](20261004) describe the game as it was on 4 October (six boxers, v2 bodies).

## Older history

Everything from 30 September to 31 October 2026 is in
[archive/README_history_2026-10.md](archive/README_history_2026-10.md), moved there word for word on
2026-11-13. It also holds the first build's reference sections: "What was built", "Measured", "Running it"
and "Things that cost time, worth knowing". The plan as it stood, with every ticked task, is
[archive/tasks_2026-10.md](archive/tasks_2026-10.md). The October training log is
[archive/rl_optimization_log_2026-10.md](archive/rl_optimization_log_2026-10.md).

## Overnight 2: the referee and card walker walk on their own, Matt punches (2026-11-04)

- Report `DOCS/reports/2026-11-04-overnight.html`; best weights `DOCS/reports/2026-11-04-overnight-weights.json`.
- **Crew:** referee and ring-card walker built from the stylised figures (`training/tools/build_crew.py`, models/crew),
  footwork trained from Sokol's and Nick's (`crewf1_*`), the referee again with his arm held (`--gesture`, `crewf2`).
  Every C MuJoCo footwork line passes; both in the game (Assets/Crew, Mj/MjCrew), 0 differences from the trainer.
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
  `DOCS/reports/2026-11-03-overnight.html`; best brains per boxer: `DOCS/reports/2026-11-03-overnight-weights.json`.
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
  Report: `DOCS/reports/2026-11-03-zombie-punching.html`.

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

