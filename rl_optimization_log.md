# RL optimisation log

Runs and decisions of the retrofit (plan: `tasks.md`), newest last. Earlier training is in `training/README.md`.

Entries from 2 to 31 October 2026 are in [DOCS/archive/rl_optimization_log_2026-10.md](DOCS/archive/rl_optimization_log_2026-10.md).

## 2026-11-01 17:33, carrying-on push v3f (owner: "do all" / "yes")
- `v3m5_sokol` (from v3m4), `v3m5_nick` (from v3m4), `v3m4_kim` (from v3m3): `--handover-share 0.75 --blend-steps 25`, 1 h,
  opponents the newest v3 brains; then `rules_v3f` (blend_steps 25 into their configs); `v3m5_trump` (from v3m_trump, the
  recipe of v3m_trump with `--weights matt=2 grandma=2 grandpa=2` this time). Exams on `logs/book_v3f.json`.
- Name clash: the exams were queued as `exam_v3f_*`, a name already used on 10-30/31, so the runner skipped them as done
  (idle 21:35-21:50); renamed `exam_v3g_*`. Queue job names must be new: check `logs/queue.state.json` first.
- Training ends: Sokol hits 0.77/s, falls 0.14 (0.18 -> 0.12 -> 0.14); Nick 0.89/s, 49% head, falls 0.07 -> 0.15; **Kim
  regressed: 0.26/s, falls 0.26 (v3m3 ended 0.53/s, 0.09)**; Trump 0.11 -> 0.06/s, falls 0.21.
- **Exams v3g (vs v3e this morning):** carrying on got worse for all three it was aimed at.

  | boxer | footing | attack | guard | chin | getting up | carrying on | vs v3e |
  |---|---|---|---|---|---|---|---|
  | Sokol | pass | pass 0.68/s 4.9 m/s | pass | pass | pass 54/60 | FAIL 44/54 (81%) | was 47/54; attack up |
  | Nick | pass | **pass** 0.87/s 4.3 m/s | pass | pass | pass 51/60 | FAIL 29/51 (57%) | was 36/55; attack now passes |
  | Kim | **FAIL 2.11/min** | FAIL 0.30/s | pass | pass | pass 54/60 | FAIL 42/54 | worse: keep v3m3 |
  | Trump (v3m5) | pass | FAIL 0.06/s | pass | pass 0.17/min | FAIL 33/60 | FAIL 26/33 | same; chin steadier |
  | Zombie | pass | pass 0.65/s | pass | pass | FAIL 42/60 | FAIL 35/42 | same brain |
  | Matt | pass | FAIL 0.24/s | pass | pass | FAIL 25/60 | FAIL 22/25 | same brain |
  | Grandpa | pass | FAIL 0.28/s | pass | pass | FAIL 0/60 | - | same brain |
  | Grandma | **FAIL 5.56/min** | FAIL 0.36/s | pass | pass | FAIL 0/60 | - | same brain, harder opponents |

  No boxer passes every line. More get-up starts made better punchers, not better carriers-on; the 0.5 s blend probably
  holds the get-up crouch too long into the bout.
- **Next, queued 23:30 (owner: "do all"):** `rules_v3h` (blend back to 15 for Sokol, Nick, Kim); `exam_v3h_sokol/nick`
  (get-up test only, tonight's brains at 0.3 s: was the blend the cause?); a new reward `--recover-w` (envs/boxing.py: in an
  episode begun from the hand-over bank, 0.2 a step for being up and recover_w x the fall penalty again for a fall in the
  first `--recover-s` 3 s; new CSV column `carry_on`, the share of bank starts up at 3 s, which read ~0.85 for Sokol's
  v3m4 in a smoke test against the exam's 0.87); `v3m6_sokol` (from v3m4), `v3m6_nick` (from v3m5), `v3m5_kim` (from
  v3m3), share 0.5, blend 15, recover-w 1.0, 1 h each; `exam_v3r_*` on `logs/book_v3r.json`.

## 2026-11-02 03:14, recover reward v3r (owner: "do all")
- `exam_v3h_*` (tonight's v3m5 brains, blend back to 0.3 s, get-up test only): Sokol carrying on 46/55 (was 44/54 at 0.5 s),
  Nick 23/47 (was 29/51). The blend length was not the cause.
- Training with `--recover-w 1.0` (share 0.5, blend 15, 1 h): `v3m6_sokol` carry_on 0.80 -> **0.95**, falls 0.11 -> 0.03,
  hits 0.86/s; `v3m6_nick` carry_on 0.89 flat, hits **1.17/s**, falls 0.06; `v3m5_kim` carry_on 0.89, hits 0.26/s, falls 0.22.
- **Exams v3r:**

  | boxer | footing | attack | guard | chin | getting up | carrying on |
  |---|---|---|---|---|---|---|
  | Sokol v3m6 | pass | pass 0.69/s 5.4 m/s | pass 47% | pass | pass 54/60 | FAIL 47/54 (87%) |
  | Nick v3m6 | pass | pass **1.47/s** 4.1 m/s, 43% head | pass 81% | pass | pass 51/60 | FAIL 23/51 (45%) |
  | Kim v3m5 | FAIL 1.78/min | FAIL 0.36/s 2.9 m/s | pass 81% | pass | pass 54/60 | FAIL 41/54 |

- **Training's carry_on (0.95) and the exam's (0.87) disagree.** The exam's get-up test drains the reserve while the get-up
  brain rises; the bank's states (and so training's hand-over starts) carry no reserve and start full. `exam_v2.py
  --getup-no-drain` is the test as the game runs it (Sim/Fighter.cs: get-up from reserve 0.5, nothing drained while rising).
  Run by hand on book_v3r, get-up test only: **Sokol carrying on PASS 53/57 (93%)** (drained: 47/55), getting up 57/60;
  Nick 31/58 no drain (17/47 drained). So under the game's rule Sokol v3m6 passes all six lines; under the exam as it has
  been run (drained) he is 2 short. Nick's gap is something else (the exam's opponent is Grandma, let go at the hand-over).
- Next (owner to choose): promote Sokol v3m6 on the game's rule (parity + gate tests first), or make the bank and training
  start hand-overs with a drained reserve so training matches the stricter exam.

## 2026-11-02 08:00, Sokol promoted on v3; tired hand-over starts (owner: "3")
- **Promoted:** Sokol on models/v3 with `v3f_sokol` (footwork), `v3m6_sokol` (match), `v3u4_sokol` (get-up, exported by
  `export_getup_v2.py` on exam_v3r_sokol). Parity 0 differences after regenerating the stale v3 fingerprints; MjParityTests
  3/3, MjGateTests 24/24. See DOCS/README.md 2026-11-02.
- **Tired starts:** `--handover-reserve X` (envs/boxing.py): a bank start begins with a reserve of 0 to X. Measured with
  learning off (lr 1e-9), v3m6_sokol v Grandma: carry_on 0.885 fresh, 0.833 with X = 0.5, so the fresh bank starts were
  part of training's 0.95 against the exam's 0.87.
- Queued: `v3m7_sokol` (from v3m6), `v3m7_nick` (from v3m6), `--handover-reserve 0.5 --recover-w 1.0`, share 0.5, blend 15,
  1 h; then `exam_v3t_*` (strict, drained) and `exam_v3tn_*` (get-up test under the game's rule) on `logs/book_v3t.json`.
  v3m7_sokol shared the GPU with Unity's play-mode tests for its first ~15 min (iteration 69 at 08:23).
- **Result (10:35):** tired starts did not move the exam's carrying-on line.

  | | strict (drained) carrying on | game rule (no drain) | attack | other lines |
  |---|---|---|---|---|
  | Sokol v3m7 | 47/54 (87%), same as v3m6 | **50/57 (88%), v3m6 was 53/57** | pass 0.58/s | all pass |
  | Nick v3m7 | 26/52 (50%), was 23/51 | 34/58 (59%), was 31/58 | pass **1.83/s** 5.0 m/s, 49% head | all pass |

  Training: v3m7_sokol carry_on 0.83 -> 0.80 (tired), v3m7_nick 0.91 -> 0.84, hits 1.30/s. Sokol v3m6 stays promoted.
  Nick's carrying on is a separate problem: ~85-90% in training against 50-59% in the exam whatever the start; next is to
  look at what puts him down in the exam's 3 s (it hands over facing Grandma, let go at that moment).

## 2026-11-02 12:15, why Nick falls after the hand-over: he gets up facing away (owner: "continue")
- `tools/carry_diag_v3.py`: the exam's get-up test under the game's rule, each hand-over measured, and each knockdown run
  twice from one seed: the opponent boxing (as the exam) and a ghost held in its guard. Nick v3m7 v Grandma, 38 hand-overs:
  up at 3 s 68% boxing, **53% with a ghost**; the opponent touched him first in 3 of 12 falls. The falls are his own.
  At the hand-over, falls v stayed up: **facing the opponent -0.07 v 0.72** (cosine), speed 0.24 v 0.15 m/s, actions at the
  clip 9% v 4%; reserve, height and tilt the same. Sokol v3m6: 38 of 39 up; the one fall faced away (-0.69) at 2 m/s.
- Cause: training's bank starts faced the other boxer within 0.6 rad (envs/boxing.py `yaw = facing + U(-0.6, 0.6)`); a
  boxer gets up facing wherever it fell. New `--handover-yaw` (default 0.6, as before): the spread for bank starts.
  Learning off, v3m7_nick v Grandma: carry_on **0.915 at 0.6, 0.53 at 3.14** (exam 50% strict, 59% game rule): training
  now shows the exam's failure.
- Queued: `v3m8_nick` (from v3m7, 1.5 h) and `v3m8_sokol` (from v3m6, the promoted one, 1 h), both `--handover-yaw 3.14
  --handover-reserve 0.5 --recover-w 1.0`; then `exam_v3y_*` (strict) and `exam_v3yn_*` (game rule) on `logs/book_v3y.json`.

## 2026-11-02 13:40, squaring up after a get-up, tried in the exam (owner: "yes" to preparing it as an option)
- `tools/exam.py --square-up v3f`: after the get-up, the footwork policy (checkpoints/v3f_NAME) turns the boxer to face its
  opponent on the turn command, as the game's walk-out does (MjBrain.Squaring: within 0.26 rad, under 0.3 m/s, after 0.5 s,
  or 3 s), the opponent held in its guard; then the hand-over as before. Off by default; nothing changed in the game.
- Game rule (no drain), 60 knockdowns: **Nick** (v3m7) 36/58 carried on (was 34/58): 43 squared up and 36 of those carried
  on (**84%**), but **15 of 58 fell while turning** (the footwork policy was taught to turn from a quiet stand, not from a
  get-up). **Sokol** (v3m6) 54/57 (was 53/57). Facing the opponent fixes the hand-over; the turn is the new weak step.
  Not put in the game. The facing lesson (`v3m8_nick`, `--handover-yaw 3.14`) attacks the same problem from the match side.
- **Result (15:46):**

  | | strict carrying on | game rule | attack | other lines |
  |---|---|---|---|---|
  | Nick v3m8 | **39/58 (67%)**, was 26/52 | **49/58 (84%)**, was 34/58 | pass 1.51/s, 54% head | all pass |
  | Sokol v3m8 | 46/54 (85%), v3m6 47/54 | 53/57 (93%), v3m6 53/57 | pass 0.72/s | all pass |

  Training ends: v3m8_nick carry_on (any facing) 0.41 -> 0.58, flat for the last 30 min; v3m8_sokol 0.43 -> 0.49, hits
  0.86 -> 0.68/s. Nick's game-rule carrying on rose 25 points; 4 more of 58 would pass (90%). Nick v3m8 with `--square-up
  v3f`: 36/58, worse (the turn is still where he falls). Sokol v3m8 ties v3m6; v3m6 stays promoted. No promotion.

## 2026-11-02 16:35, Nick promoted on v3 (owner: "import brains and test scene")
- Nick on models/v3 with `v3f_nick` (footwork), `v3m8_nick` (match, the facing lesson), `v3u4_nick` (get-up, exported on
  exam_v3y_nick). Five of six lines; carrying on 84% under the game's rule (49/58), 67% strict. Promoted on the owner's
  word below the 90% mark. Parity 0 differences; MjParityTests 3/3; MjGateTests 24/24 (Nick's and Sokol's three each).
- In the game: Nick v Sokol, **Nick by KO in R2** (102 landed to 100, Sokol down twice, Nick never down). A forced
  knockdown (`down 0`) on Nick: he got up, then went down twice more within 25 s (Sokol credited with one): TKO R1. The
  hand-over is still his weak moment. Free-for-all: no errors.

## 2026-11-02 20:00, overnight: Nick's staying-up lesson, the other six toward v3, a referee check (owner: "yes")
- Game-rule get-up exams (`exam_v3zn_NAME`, `--skip box chin --getup-no-drain`, book_v3y) for the six still on v2:

  | | getting up | carrying on | the strict lines (exam_v3g) |
  |---|---|---|---|
  | Zombie (v3u4/v3m4) | pass 56/60 | pass 51/56 (91%) | footing, attack, guard, chin pass |
  | Trump (v3u4/v3m5) | pass 56/60 | pass 53/56 | attack FAIL 0.06/s |
  | Matt (v3u4/v3m4) | FAIL 31/60 | FAIL 25/31 | attack FAIL 0.24/s |
  | Kim (v3u3/v3m3) | pass 60/60 | FAIL 51/60 (85%) | footing FAIL 2.1/min, attack FAIL 0.30/s |
  | Grandma, Grandpa (v3u4) | FAIL 0/60 | - | attack FAIL |

- **Zombie promoted on v3** (all six lines, on Sokol's standard): `v3f_zombie`, `v3m4_zombie`, `v3u4_zombie` (get-up
  exported on exam_v3zn_zombie). Parity 0 differences; reference 250 steps up; 0% of 25 fall episodes down.
- Queued: `v3m9_nick` (from v3m8, `--recover-w 2.0`, any facing, 2 h) and its exams on `book_v3z`; `v3u5_grandma`,
  `v3u5_grandpa` (2 h), `v3u5_matt` (1.5 h): get-up from v3u4 with the hand fading more slowly (`--fade-above 0.6`, was
  0.45); `v3m6_trump` (from v3m5, 2 h, `--handover-yaw 3.14`; v3m5 ended at 0.06 hits/s, 21% falls, still climbing);
  then `exam_v3wn_*` (game rule) and `exam_v3w_trump` on `book_v3w`. About 10 h.
- Game side: `Bout.refereeCheckSeconds` (2 s). After a get-up the count phase holds on while the referee looks the boxer
  over: the opponent stays held in its guard (`MjBrain`), no punch scores, a fall in that time continues the count. Forced
  knockdowns on Nick v Sokol: once he stayed up for the rest of the round (before: TKO); once he fell twice more after the
  check (TKO R2). It removes the punch at the hand-over, not the falls of his own after it.
- **v3m9_nick result (22:24):** training carry_on 0.59 -> 0.66 (2 h, 942 iterations, hits 1.26/s, falls 0.22 -> 0.19).
  Exam: **game rule getting up 58/60, carrying on 56/58 (97%; v3m8 49/58)**; strict: footing, attack 1.68/s at 4.1 m/s,
  guard 65%, chin, getting up 49/60 pass, carrying on 38/49 (78%; v3m8 39/58 = 67%). **Nick v3m9 passes all six lines on
  Sokol's standard** (the game's get-up rule): candidate to replace v3m8 in the game.
- Correction: match runs train at 14-15k samples/s with or without Unity (v3m5_trump, v3m8_nick, v3m8_sokol all 14-15k);
  the ~60k figure is the footwork env. v3m9_nick was not slowed by the editor.
- **v3m6_trump stopped at ~1.3 h (owner away; unattended call):** reward fell from iteration ~120 on (ep_return 237 ->
  123), falls 0.43 -> 0.72, carry_on 0.25 -> 0.16, hits flat at 0.04-0.06/s. The any-facing get-up starts
  (`--handover-yaw 3.14`) that lifted Nick pulled Trump down; the good early checkpoints were pruned (kept: 450+). Stopped
  with `logs/v3m6_trump.stop`, its exams taken off the queue, book_v3w's Trump back on v3m5; v3m5 stays his best match brain.
- **v3u5_matt (01:12):** training unaided get-up 0.05 -> ~0.30, assist 0.20 -> 0.13 (1.5 h, 2410 iterations). Exam,
  game rule: **getting up 54/60 (was 31/60), carrying on 53/54 (98%)**: both pass. Matt still fails attack (0.24/s strict,
  exam_v3g) on v3m4, so not yet a full six-line pass.
- **v3u5_grandma (03:14):** 2 h, 3357 iterations; training unaided get-up stayed ~0 (peak 0.003), assist 0.36 -> 0.24,
  aided up rate ~0.46. Exam, game rule: getting up **5/60** (was 0/60; mark 60% within 9 s), carrying on 2/5. Fails. The
  slower fade alone does not get her up; her get-up needs another approach (strength/pose, or a different partner start).
- **v3u5_grandpa (05:15):** 2 h, 3302 iterations; training unaided ~0, assist 0.35 -> 0.29, aided up rate 0.35 -> 0.49.
  Exam, game rule: getting up **14/60** (was 0/60), carrying on 9/14. Fails.
- **v3m6_trump, why:** its value_loss climbed from ~150 to over 500,000 in the last 20 minutes (the critic diverged)
  while the reward fell; the any-facing get-up starts made his episodes much shorter and harder to value.
- **Night summary (05:20):** passes all six lines: Nick v3m9 (new), Zombie v3m4/v3u4 (found on the game rule), Sokol
  (in the game). Matt's get-up now passes (v3u5, exported); his attack still fails. Trump, Kim, Grandma, Grandpa stay on
  v2. Exports: `v3u5_matt` get-up ONNX; Grandma/Grandpa not exported (the export gate refused their failing exams).
  Manifest `DOCS/reports/2026-11-03-overnight-weights.json` (copy of training/logs/overnight_2026-11-03_weights.json);
  report `DOCS/reports/2026-11-03-overnight.html`. Nothing promoted into Unity unattended.

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

## 2026-11-03 22:20, overnight 2: the crew's footwork, then punching under pressure

- **Crew bodies** (owner: "use rl training for ringcard girl and referee"): rigs from the stylised figures
  (`glb_to_rig.py` on Assets/Art/Exports/GLB/SK_Referee, SK_RingCardWalker), built the v3 way by the new
  `tools/build_crew.py` (human force, per-joint action, de Leva masses, damping standing, Nick's rules of a living body)
  into `models/crew`, with their own rest pose (`rig_to_mjcf.py` "stance_deg": the referee's arms hanging, the walker's
  right arm up with the card). The walker's thighs were 7 mm inside her torso in every pose: thigh radius 0.0735 -> 0.065.
  Both clear the self-contact check; Unity compiles both with 0 differences from the trainer.
- `crewf1_referee` (from v3f_sokol) and `crewf1_cardwalker` (from v3f_nick): footwork, 1 h each, then footwork_c exams.
- **exam_v3r_all** (rounds 2, new "under pressure" line): 0 of 8 pass. The hardest opponent is Matt for six of them: his
  v3m4 brain lands 0.18/s and keeps away, so nobody lands on him either. Attack fails for Trump (0.03/s), Matt (0.18),
  Kim (0.39), Grandma (0.35), Grandpa (0.22); footing for Kim, Grandma, Grandpa.
- Queued: `v3p1_matt`, `v3p1_trump`, `v3p1_kim` (v3m6_zombie's recipe: hit_w 2.2, taken_w 0.7, block_w 0.25,
  --lr-max 6e-5, 1 h; each boxer's opponents it landed under 0.25/s on drawn 3x), then their exams.
- Not queued: Grandma's and Grandpa's get-up. v3u5 ended with the helping hand stuck at 22-24% and 0% unaided: more of the
  same will not do it; strength (their "age") or getting up off the ropes is the owner's call.
- **23:35** `crewf1_referee` (1600 it, 0.89 h): footing exam in C MuJoCo 3.5.0 passes every line (stand 100%, walk off by
  0.02 m/s with no falls, turn 100%, joints at 0.35 of their speed limit). Promoted into his Unity body (Assets/Crew,
  not a boxer). In the game he walked to his spots but fell: (1) the steering turned him out of mid-stride whenever the
  moving target drifted (never trained: a turn always starts from standing) -> MjCrew.GoTo now stops before it turns, the
  goal moves only when the fight moves it 0.6 m, spots 0.9 m inside the ropes, 0.6 m/s; (2) with his right arm held up over
  the brain at the result he still fell twice in 36 s. RL fix: `envs/footwork.py --gesture SHARE` (new): in that share of
  episodes the right arm is held as the game holds it (the count's stroke once a second, or straight up) and those joints
  do not count against the arms term. Queued `smoke_gesture` (2 iterations) and `crewf2_referee` (from crewf1, gesture
  0.35, 0.75 h) after the walker's exams. Game-side safety net: a referee down is stood back in his corner at the next
  walk-on, intro or break, or after 4 s.
- **00:31** `crewf1_cardwalker` exam passes every line (stand 100%, walk off by 0.008 m/s, no falls, turn 100%, joints 0.48);
  promoted into her Unity body. `smoke_gesture` ran clean (deleted). `crewf2_referee` (gesture 0.35) at iteration 2762:
  falls 0.0%, turns 100%, arms term steady.
- **01:31** `crewf2_referee` (gesture 0.35, to iteration 3000): exam passes every line (stand 100%, walk off 0.009 m/s, no
  falls, turn 100%, joints 0.36); promoted into his Unity body. `v3p1_matt` at 0.83 h: hits 0.02 -> 0.47/s (Kim 1.20,
  Nick 0.86, Trump 0.60, Grandma 0.67, Grandpa 0.31, Sokol 0.17, Zombie 0.03), falls 0.45 -> 0.09, value loss 62 -> 143
  (no runaway).
- **02:31** `v3p1_matt` (1.0 h): exam attack 0.18 -> 0.62/s (fails on speed, 3.8 m/s against 4.0; all body), under
  pressure 0.02/s against Sokol and Zombie. Queued `v3p2_matt` from it (zombie=4 sokol=4 nick=3 trump=3).
  `v3p1_trump` **stopped** at 0.7 h: it resumed v3m5's critic with a value loss of 2.2e5 already; it climbed to 2.5e6,
  falls rose 0.34 -> 0.48 and hits stayed 0.04/s. New `train_gauntlet.py --reset-critic` (a fresh critic over the
  resumed actor, its Adam state dropped); queued `v3p2_trump` with it after Kim.
- **03:31** `v3p1_kim` (1.0 h): its critic ran away like Trump's (value loss 51 -> 1,331 at it 350), hits peaked at 0.27/s
  (it 200) and fell to 0.20, falls 0.22 -> 0.25. Only its 400-460 checkpoints survive the pruning (little between them):
  its exam takes the latest. Queued `v3p2_kim` (from v3m3, --reset-critic, --lr-max 3e-5, fall penalty 18) after
  v3p2_matt: the queue holds work to about 06:50.
- **04:31** `v3p2_trump` (--reset-critic): the fresh critic held (value loss 38 -> 15 through 0.5 h; a late spike to 1.3e4
  in the last minutes), falls 0.57 -> 0.19-0.24, but hits stayed 0.05/s against everyone. Stability was not all of it:
  his reach is the roster's shortest (0.44 m shoulder to wrist, roster.json) and at range_m 0.60 he lands on boxers that
  come to him. Next for him (morning proposal): a closing-distance lesson (press / range in his style), not more of this.
- **05:31** `v3p2_matt` at 0.97 h: hits 0.72/s (Sokol 0.02 -> 0.47, Nick 1.33, Trump 1.11, Kim 1.10), Zombie still 0.04;
  falls 0.09, value loss 6-8 throughout (lr-max 6e-5, no runaway). Queued `v3p3_matt` (from it, zombie=6 sokol=2, 0.75 h)
  after v3p2_kim to keep the GPU busy to about 07:40.
- **06:31 exams** (rounds 2, 60 get-up trials, game rule): `v3p2_matt` **attack passes** (0.66/s at 4.3 m/s; v3m4 0.18 at
  3.0), footing and guard pass, under pressure 0.02/s against Zombie only (Sokol now 0.47 in training). Its get-up lines
  failed because the book still pairs it with v3u4_matt (34/60); v3u5_matt passed 54/60 last night. `v3p1_matt`: attack
  0.62 at 3.8 m/s (fails on speed). `v3p2_trump`: attack 0.05/s, guard now FAIL (21%): kept on v3m5. `v3p1_kim`: attack
  0.42 (0.39 before), footing worse (2.0 falls a minute): kept on v3m3. `v3p2_kim` (fresh critic, lr-max 3e-5): hits 0.12/s
  and its value loss ran away again (1.3e5) near the end: Kim and Trump do not learn to punch from this recipe; their
  problem is elsewhere (Kim's footing, Trump's reach). `v3p3_matt` (Zombie) runs next to about 07:35.
- **07:31** `v3p3_matt` (Zombie 6x, 0.75 h) made him worse (hits 0.48 -> 0.40, Zombie 0.08, value loss to 2.1e3; exam
  attack 0.51 at 3.4 m/s fails): v3p2 stays. **v3p2_matt with v3u5_matt's get-up** (exam_v3p2u5_matt): footing, attack
  (0.66/s at 4.3 m/s), guard, getting up 60/60, carrying on 59/60 all pass; under pressure 0.02/s against Zombie.
- **Night summary:** crew trained and in the game (referee crewf2, walker crewf1, every footwork line passes). Matt passes
  the six original lines (proposed for promotion). Trump and Kim: the Zombie/Matt recipe does not work for them, with or
  without a fresh critic. Report `DOCS/reports/2026-11-04-overnight.html`, weights `DOCS/reports/2026-11-04-overnight-weights.json`.
- **2026-11-04 morning (owner: "ok")** Matt promoted on the v3 body: footwork v3f_matt (exam passes; references recorded
  in C MuJoCo 3.5.0: walk, falls 0% of 25, hold), boxing v3p2_matt, get-up v3u5_matt. Parity 0 differences; MjParity 3/3,
  MjGate 24/24, StateFlow 2/2, OneScreen 74/74. Bouts: v Sokol 25 of 30 landed to 1 (decision), v Zombie a stand-off
  (5 and 3 thrown), as the exam's under-pressure line said.
- **Next lessons (owner: "yes"):** `v3c1_trump` closing distance: his style press 0.4 -> 1.2 and range_m 0.60 -> 0.50
  (models/v3/trump_policy_config.json; paid for walking in on the target from outside its range), from v3m5 with a fresh
  critic, lr-max 6e-5, 1 h. `v3f2_kim` footing in the bout: from v3m3, fall penalty 12 -> 30, hit_w 2.2 -> 1.5,
  taken_w 1.0, fresh critic, lr-max 3e-5, 1 h. (Her footwork brain passes its exam; the falls are the boxing brain's.)
- **10:39 exams** (rounds 2, 60 get-up trials): `v3c1_trump` (closing distance): attack 0.11/s at 3.3 m/s (v3p2 0.05; mark
  0.5 at 4.0), guard back to PASS (49% stopped; v3p2 21%), footing, getting up 59/60, carrying on 55/59 pass; under
  pressure 0.00 against Grandma. Better than v3m5/v3p2 but not good enough to promote; kept on file. `v3f2_kim` (fall
  penalty 30): footing still 2.0 falls a minute, attack 0.07/s at 1.4 m/s (v3p1 0.42), carrying on 50/60 FAIL: the lesson
  made her worse; she stays on v3m3. Neither promoted.
- **2026-11-04 afternoon (owner: "promote brains")** Trump moved to the v3 body (MjRetrofit OnV3): footwork v3f_trump
  (exam passes; references recorded in C MuJoCo 3.5.0: walk, falls 0% of 25, hold), boxing v3c1_trump (attack 0.11/s,
  guard, footing, getting up 59/60, carrying on pass), get-up v3u4_trump (exported to ONNX here; it had none). Check:
  0 differences. Kim not promoted: v3f2 is worse than v3m3, and her v3 stack still fails footing (2.0 falls a minute).
- **Kim promoted (owner: "put kim on new brain")** onto the v3 body: footwork v3f_kim (exam passes; references in C
  MuJoCo 3.5.0: walk, falls 0% of 25, hold), boxing v3m3_kim (her best v3 boxing: v3p1 and v3f2 were worse), get-up
  v3u3_kim (exported to ONNX here). Check: 0 differences. Her boxing still fails footing in the exam (about 2 falls a
  minute) and carrying on (49/60). Trump checked: already on his newest brains (v3f, v3c1, v3u4).

## 2026-11-13 10:40, a new PC, and the grandparents toward v3 (owner: "do 1 2 3 4 5 6 7 8 9 12 13 16 17 18 19 20" of the top-20 list)
- **This PC:** a fresh clone on a laptop (i7-10750H, 16 GB, RTX 2060 6 GB). No checkpoints, logs or `.mj350` came with it.
  Trainer Python: `../PoOlympic/training/.venv` (3.11.17, mujoco 3.11.0, mujoco_warp 3.11.0, warp 1.17.0, torch 2.14.0+cu130).
  MuJoCo 3.5.0 for the exams: `uv pip install --python <venv python> --target training/.mj350 mujoco==3.5.0`.
  Speed here: match gauntlet 8.3k samples/s at 2352 rings (the old PC 14-15k), footwork 23-35k (the old PC ~60k).
- **Warm starts:** every shipped brain rebuilt into `checkpoints/onnx/{footwork,match,getup}_NAME.pt` (`tools/onnx_to_pt.py`; the
  grandparents' match brains with `--obs-state`). No v3 run of the old PC is on this disk: the grandparents' v3f/v3u/v3m
  checkpoints are gone, so they start again from their shipped v2 brains rescaled (`checkpoints/v3/*_grandma|grandpa.pt`).
- **Where the roster stands (configs):** Matt, Zombie, Nick, Sokol, Kim, Trump on the v3 body (fatigue 8000, ceiling, no
  obs_state); Grandma and Grandpa on v2 (fatigue 20000, obs_state, Grandma `getup_speed_share` 0.5).
- **Why the grandparents' v3 get-up failed (0/60, then 5/60 and 14/60 after v3u5):** Grandma's v2 lesson of 2026-10-18 (she
  cannot rise under the full speed limit; the game holds it at half while her get-up brain has the body) was never carried to
  v3: no `getup_speed_share` in any models/v3 config, no `--speed-share` in the v3u recipes. Measured today (C MuJoCo 3.5.0,
  game rule, 20 knockdowns, shipped v2 get-up brains rescaled to v3): Grandma 0/20 at share 1, 2/20 at 0.5, 1/20 at 0.25,
  2/20 at 0; Grandpa 0/20 at every share. So the rescale alone does not carry a get-up to v3 either: both need the lesson.
- `tools/set_rules.py --getup-speed-share` (new). **`getup_speed_share` 0.5 set for Grandma and Grandpa in models/v3**
  (15 configs). For Grandpa this is a new rule (on v2 he rises under the full limit, 49/60): the owner's call to keep.
- **Queued** (`logs/queue.json`): `v3f_grandma`, `v3f_grandpa` (footwork from the rescaled brains, 0.5 h each, the staged
  recipe) and their footwork exams; `v3u6_grandma`, `v3u6_grandpa`: get-up from the rescaled brain, Matt's shipped v3 get-up
  frozen as the partner, `--speed-share 0.5 --no-drain --fresh-assist NAME --fade-above 0.6 --lr 3e-5`, std 0.3 (max 0.4),
  1.5 h each; then `exam_v3u6_*` (60 knockdowns, game rule, `logs/book_u6.json`).
- Baseline exam of all eight on models/v3 with the brains on this disk: `logs/exam_base_1113.json` (book_base, rounds 2).

## 2026-11-13 Sprint Update (finished before 14:30)
- **Footwork v3 exams (`models/v3`):**
  - `v3f_grandma` (550 it, 0.5 h): PASS 4/4 (`stand 1.00`, `walk err 0.0121 / falls 0.0`, `turn 0.90`, `elbow_r 0.423`). Archived `pass-v3f-grandma`.
  - `v3f_grandpa` (468 it, 0.5 h): 3/4 (`turn 0.8333` short of `0.90`). Resumed in `v3f2_grandpa` (+0.1 h, to it 565, `--walk-share 0.4 --turn-share 0.4`): PASS 4/4 (`stand 0.9667`, `walk err 0.0180 / falls 0.0`, `turn 0.9667`, `knee_l 0.520`). Archived `pass-v3f2-grandpa`.
- **Get-up & match handover (`models/v3`, `getup_speed_share 0.5`):**
  - `v3u6_grandma` (1753 it, 1.50 h): PASS getting up (`54 of 60`, avg `3.6 s`); `carrying on` `25/54` against un-retuned `match_grandma.pt`. Archived `v3u6-grandma-getup90`. Handover bank `logs/handover_v3_grandma.npz` (800 states) built via updated `tools/make_handover_bank.py --models`.
  - `v3u6_grandpa`: time-boxed stop at it 293 (0.23 h, archived `v3u6-grandpa-it293`, partial `1/60`).
  - `v3m_grandma` (handover + `--blend-steps 15`, 31 it, 0.07 h): too short to reach boxing competence from footwork (`0.00/s` hits, `12.00/m` falls, `3/27` carry-on, `27/30` get-up PASS, `29%` guard PASS). Archived `sprint-v3m-grandma`. Needs full 1 to 2 h run.
- **Trump closing distance (`v3c2_trump`):** Synced `rigs/v3/trump.json` + `trump*_policy_config.json` via `tools/set_rules.py --style` (`range_m=0.48, press=1.5, pace_s=0.45, body=1.0, taken=0.4`) and fixed `exam.py` `action_filter` parity. 43-it probe (`0.07 h`) lifted head share `43% -> 64%`, attack speed `2.3 -> 3.0 m/s`, and pressure `0.00 -> 0.02/s`, with footing regression (`1.33/m`) and carry-on `26/29`; kept shipped `v3c1` (archived `probe-v3c2-trump`).
- **Infra, tooling & smoke checks:**
  - Verified `tools/smoke_realism.py` (`exit 0`, all checks passed) and `F4` multi-body `getup` (`smoke_f4_getup`, 10 it, `bodies=2`, `exit 0`).
  - Added `tools/ffa_exam.py` (3-bout `ffa_base_1113.json`: `7.46` slips/boxer/m, `31.29` hits/boxer/m, `40` KDs, `50%` beat count), `tools/archive_checkpoints.py`, `rig_to_mjcf.py` + `build_v3.py --neck`, and `envs/boxing.py` split (`math3d.py`, `boxing_base.py`, `boxing.py`).
  - Refreshed `Assets/Resources/cpu_elo.json` (`tools/elo.py --models models/v3 --book logs/book_base.json --bouts 2 --workers 3`, 56 bouts, 439 s): Nick 1242, Grandma 1154, Kim 1127, Sokol 1075, Matt 976, Zombie 847, Grandpa 789, Trump 789.
- **Unity, Android, Art:**
  - `PoBox/Assets/Scripts/Runtime/{Audio,UI}` split; `PoBox/Packages/org.mujoco` test cleanup fix; `Tools/run_tests.ps1` `124/125` passed (`FreeForAllTests 0/1`: 3/8 boxers landed vs `>=4`).
  - `Assets/Models/{Grandma,Kim,Sokol}.glb` updated and weight-checked (`SCREENSHOTS/2026-11-13-hands-hair/report.html`).
  - Android APK built (`PoBox/Build/Android/PoBox.apk`, 340,460,565 bytes; 889.27 MB raw packed GLB textures flagged as primary cut); ADB deploy blocked with no device attached.
  - Report: `DOCS/reports/2026-11-13-sprint.html` with charts in `DOCS/reports/2026-11-13-sprint-charts/`.
- **Results by 14:30** (queue.log; all exams C MuJoCo 3.5.0):
  - `v3f_grandma` (0.5 h, 550 iterations, 23-35k samples/s): footwork exam passes every line (stand 100%, walk off by 0.01 m/s,
    no falls, turn 90%, joints 0.42 of the limit).
  - `v3f_grandpa` (0.5 h, 468 it): stand 100%, walk passes, **turn 83% FAIL** (mark 90%). `v3f2_grandpa` (from it, 0.1 h,
    `--walk-share 0.4 --turn-share 0.4`): stand 97%, walk passes, turn 97%, joints 0.52: passes.
  - **`v3u6_grandma` (1.5 h, 1753 it, half speed limit while rising): getting up 54 of 60 in 3.6 s: passes** (mark 60% within
    9 s; the old PC's v3u5 without the share: 5/60). Carrying on 25/54 with her rescaled v2 match brain: fails, her match lesson next.
  - `v3u6_grandpa` was stopped by a stop file at 0.23 h (iteration 293): 1/60. Carried on below as `v3u7_grandpa`.
  - Baseline, the brains on this disk on models/v3, rounds 2, 30 knockdowns, game rule (`logs/exam_base_1113.json`): no boxer
    passes "under pressure" (0.00-0.07/s on the hardest opponent, Matt for five of them). Attack: Zombie 0.90/s, Nick 0.93,
    Sokol 0.69 pass; Matt 0.55 at 3.8 m/s (speed), Kim 0.44, Trump 0.11, Grandma 0.16, Grandpa 0.06 fail. Footing fails for
    Kim (3.3 falls/min), Grandma (11.3) and Grandpa (2.7) (the last two on rescaled v2 brains). Head share: Matt 0%, Sokol 0%.
  - Also run in a stretch of this session whose notes were lost (13:45-14:30; the other open session, codematt-4a, confirms it
    did none of it): `v3c2_trump` (0.07 h; attack 0.11/s, footing 1.33 falls/min: no gain) and `v3m_grandma`
    (0.07 h; attack 0.00): neither usable. `Assets/Resources/cpu_elo.json` was also written at 14:29 from 2 bouts a pair with the
    grandparents on their rescaled v2 brains on v3 bodies, which the game does not run: **not a valid ladder; do not commit it**.
    The Elo is re-run (6 bouts a pair) once both grandparents have v3 brains.
- **Queued 14:33 (batch 3):** `v3u7_grandpa` (from v3u6_grandpa's iteration 293, same recipe, 1.5 h) + exam;
  `bank_v3b_grandma` (hand-over bank from v3u6_grandma); `v3m2_grandma` (the staged v3m recipe from her rescaled match brain,
  against the six shipped v3 brains and Grandpa's rescaled one, 2304 rings, `--lr 3e-5 --lr-max 6e-5 --handover-share 0.5
  --handover-yaw 3.14 --recover-w 1.0 --blend-steps 15`, 1.5 h) + full exam on `logs/book_b3m.json`.
