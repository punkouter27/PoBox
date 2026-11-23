# RL optimisation log

Runs and decisions of the retrofit (plan: `tasks.md`), newest last. Earlier training is in `training/README.md`.

Entries from 2 to 31 October 2026 are in [archive/rl_optimization_log_2026-10.md](archive/rl_optimization_log_2026-10.md).

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
  3/3, MjGateTests 24/24. See docs/README.md 2026-11-02.
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
  Manifest `docs/reports/2026-11-03-overnight-weights.json` (copy of training/logs/overnight_2026-11-03_weights.json);
  report `docs/reports/2026-11-03-overnight.html`. Nothing promoted into Unity unattended.

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
  without a fresh critic. Report `docs/reports/2026-11-04-overnight.html`, weights `docs/reports/2026-11-04-overnight-weights.json`.
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
  - Report: `docs/reports/2026-11-13-sprint.html` with charts in `docs/reports/2026-11-13-sprint-charts/`.
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

## 2026-11-15 18:20, three hours on the grandparents' boxing brains (owner: "training as needed next 3 hours", PoBox)
- **Why:** V1 is the front. Both grandparents now have v3 get-ups that pass (`v3u6_grandma` 54/60, `v3u7_grandpa` 59/60), but
  their boxing brains are the rescaled v2 ones (carrying on 25/54 and 48/59; footing 11.3 and 2.7 falls a minute). Batch 3's
  `v3m2_grandma` was queued on 2026-11-13 but never ran (no checkpoint, not in queue.state).
- **Queued (`logs/queue.json`):** `bank_v3_grandpa` (hand-over bank from `v3u7_grandpa`, 800 states; Grandma's bank from
  `v3u6_grandma` already exists, so `bank_v3b_grandma` was dropped); `v3m2_grandma` (the batch-3 recipe: from her rescaled
  match brain, `--obs-state`, fresh critic, `--lr 3e-5 --lr-max 6e-5`, `--handover-share 0.5 --handover-yaw 3.14 --recover-w 1.0
  --blend-steps 15`, `--fall-penalty 12`, 2304 rings, against the six shipped v3 brains + Grandpa's rescaled one) cut from 1.5 h to
  **1.1 h** to fit both in the window; its exam (30 knockdowns, `logs/book_b3m.json`); `set_rules_grandpa` (blend-steps 15);
  `v3m2_grandpa`, the same recipe against the six + the new `v3m2_grandma`, 1.1 h, and its exam (`logs/book_b3p.json`).
- Machine: RTX 2060 6 GB, Unity closed, no other session on the GPU at 18:20.
- **19:33 `v3m2_grandma` (1.1 h, 554 it, ~7.9k samples/s) exam, 30 knockdowns:** footing 10.67 falls/min FAIL (base 11.3), attack
  0.01/s at 1.1 m/s FAIL, under pressure 0.00 FAIL, guard PASS (46% stopped), chin PASS (0.19/min), getting up 26/30 in 3.7 s PASS,
  carrying on 12/26 FAIL (base 25/54, same 46%). Training: falls 24% -> 86% when hand-over episodes began, best 42% at it 350, back to
  61% at the end; she learned to block (0.03 -> 0.75/s) and stopped punching. **Not promoted.** Reading: the rescaled v2 match brain
  falls over by itself on v3 and 1.1 h does not mend it. Her `v3f_grandma` footwork passes (stand 100%, no falls).
- **Queued 19:35 `v3m3_grandma`** (0.5 h probe, after Grandpa's run): same recipe, warm start from `v3f_grandma/latest.pt` instead,
  no `--obs-state` (her v3 config has none), `--fall-penalty 20`, Grandpa's new `v3m2_grandpa` as the 7th opponent; exam on `logs/book_b3g.json`.
- **20:42 `v3m2_grandpa` (1.1 h, 549 it, ~8k samples/s) exam, 30 knockdowns:** footing **16.0** falls/min FAIL (base 2.7: worse),
  attack 0.01/s at 0.5 m/s FAIL, under pressure 0.00 vs Kim FAIL, guard PASS (57% stopped, 0.14 head shots/s), chin PASS,
  getting up 30/30 in 2.9 s PASS, carrying on 21/30 FAIL (base 48/59). **Not promoted.** Training: falls 9% -> 90% (hand-over
  episodes) -> best 44% at it 250, then back to 82% at the end; return 210 -> 63.
- **Why both v3m2 runs fell apart: the critic diverged.** `value_loss` in both CSVs climbs geometrically from about it 275 while
  the episode return stays bounded: grandma 30 (it 220) -> 148 -> 3.7k -> 32k -> 101k -> 442k -> **1.49M** (it 550); grandpa
  170 (it 250) -> 810 -> 3.4k -> 11k -> 32k -> 96k -> 221k -> **741k** (it 410). Both policies peak at it 250-350 and degrade
  from there. kl stays 0.006-0.009 and lr sits at lr-max 6e-5, so the actor's steps are not the cause. `v3c2_trump` (11-13,
  43 it) was already rising 29 -> 272. Suspect: the time-out / survivor bootstrap in `ppo.py` `_gae` (`rew + gamma*val[t]*timeout`
  feeds V(s_t) into its own target) with a fresh critic, `--handover-share 0.5` (many short bank episodes ending in falls or cuts)
  and `--recover-w 1.0`. **Not fixed tonight (core trainer change; owner's call).** Rule until then: in a gauntlet run with
  `--reset-critic`, watch value_loss; anything over ~10x its it-100 value means stop and keep the last good save.
  Saves on disk only from it 450/500, already past the turn: none are worth examining.
- **20:55 trainer change (owner: "do recommended"):** `ppo.py` `PPOConfig.value_huber` (opt-in Huber critic loss, delta in return
  units; the logged `value_loss` stays the plain MSE so CSVs compare) and `train_gauntlet.py --value-huber`, plus a divergence guard
  on by default: `--vloss-guard 10 --vloss-window 10` stops the run *without saving* once the value loss stays over 10x its baseline
  (median of the run's iterations 50-150), so the last periodic save is the pre-divergence brain. On reading `_gae` again, the
  time-out bootstrap `rew + gamma*V(s_t)*timeout` is the rsl_rl convention (the env auto-resets, so the true terminal state's value is
  not available); bootstrapping from the terminal observation would need `envs/boxing.py` to observe before reset (an extra
  observe a step). Not done: the Huber loss and the guard come first, and the next runs will show whether that is enough.
- **Queued:** `v3m4_grandpa` (the v3m2 recipe + `--value-huber 10 --save-every 25`, against the six shipped v3 brains + Grandma's
  rescaled match brain, 1.1 h) + exam on `logs/book_b4p.json`. Grandma's re-run is decided on the `v3m3_grandma` probe's exam.
- **21:14 `v3m3_grandma` probe (0.5 h, 249 it, from `v3f_grandma`, fall penalty 20, no obs-state) exam:** footing **6.67**
  falls/min (v3m2 10.67), attack 0.00, under pressure 0.00, guard PASS (36%), chin PASS (0.00), getting up 26/30 PASS, carrying on
  13/26. Training falls stayed 74-87% (the hand-over episodes) and value_loss began to climb at it 240 (35 -> 149): the critic
  turn comes at ~250 with or without obs-state and at fall penalty 20, so it is not the recipe's reward terms.

## 2026-11-15 21:15, ten hours overnight (owner: "training as needed for next 10 hours")
- Every gauntlet run from here: `--value-huber 10 --save-every 25`, guard on (default). Queue, about 21:15 -> 02:45:
  `v3m4_grandpa` (1.1 h) + exam; `v3m4_grandma` (resumes `v3m3_grandma` with its critic, 1.1 h) + exam; `v3c3_trump` (V2: the
  v3c2 recipe, which in 0.07 h lifted head share 43 -> 64% and pressure 0 -> 0.02, now 1.5 h; style already range 0.48 / press 1.5)
  + exam; `bank_v3c_kim` (from her shipped v3 get-up) and `v3m4_kim` (V3: from her shipped `v3m3_kim`, fresh critic, lr 1e-4..2e-4,
  hand-over 0.5, recover-w 1, blend 15, 1.5 h) + exam. Note: on 11-01 `v3p2_kim`'s value loss also ran away (1.3e5), so "Kim and
  Trump do not learn from this recipe" may have been the critic too.
- Exam books `logs/book_n4g|n3t|n4k.json`: the grandparents' opponents are their rescaled v3 match brains (the v3m2 brains are broken).
- 02:45 -> ~06:30: follow-ups chosen on these exams; report and commit by ~07:00.
- **22:05 `v3m4_grandpa`** (Huber 10, guard): **the guard stopped it at it 410** (value loss 571 vs baseline 42.5); the save at it 400
  is the brain. The Huber loss put the turn off from ~275 to ~350 but did not remove it. Training: falls 89% -> 45%, carry-on 0.26 ->
  0.52, return 73 -> 250. Exam (30 KDs, `book_b4p`): footing 4.00/min FAIL (old brain 2.7, v3m2 16.0), attack 0.03/s FAIL, under
  pressure 0.00 FAIL, guard PASS 64%, chin PASS, getting up 30/30 in 2.5 s PASS, carrying on 25/30 (83%; old 81%, v3m2 70%) FAIL.
  Best v3 Grandpa match brain so far; not promoted (footing). Queued `v3m5_grandpa`: from its it-400 save with a *fresh critic*
  (restarts the critic's clock), 1.0 h, + exam on `book_n5p`.
- **22:41 `v3m4_grandma`** (resumed `v3m3_grandma` with its 249-it critic, Huber 10): **guard stop at it 274** (value loss 1176 vs
  66.6). Exam (`book_n4g`): footing 6.67 FAIL, attack 0.00 FAIL, guard PASS 43% (0.07 blocks/s), chin PASS, getting up 27/30 in
  4.2 s PASS, carrying on **8/27** FAIL (probe 13/26). No gain; `v3m3_grandma` stays her best v3 match brain. Across five runs the
  critic turns ~250-350 iterations after it was made, whatever the recipe: it is the critic's age, not the lesson. Grandma gets no
  more GPU tonight: her body (strength 0.53, speed 0.75 on v3) may be the limit, which is the owner's call (tasks.md V1).
- **23:00 diagnosis (CPU, while Trump trains):** the observation normaliser is not it (count ~1e9 from the ONNX rebuilds, so it is
  effectively frozen; smallest variance 6e-4, none near zero). The critics are: on the same 4096 random inputs, the diverged ones
  predict V mean -1319 / -2216 (v3m2_grandma it 500 / 554, min -18k) and -4730 (v3m2_grandpa it 549, min -43k), last-layer
  weight norms growing 2.9 -> 3.7; the guarded v3m4_grandpa it 400: mean -31, min -312. Returns are ~+200: the critic runs away
  *downward*. Rewards are bounded, so the suspect is the self-referential `gamma*V(s_t)` added at time-outs and survivor cuts.
  New opt-in `PPOConfig.bootstrap_cuts` / `train_gauntlet.py --no-cut-bootstrap` (such steps valued as terminal). **Test:**
  `v3m5_grandpa` now runs with it (fresh critic, guard on). If its critic stays sane past it ~350, that is the cause.
  Kim's `v3m4_kim` also gets `--no-cut-bootstrap`; `v3c3_trump` (running, bootstrap on) is the control.
- **22:57 `v3c3_trump`** (control, bootstrap on, lr 3e-4..5e-4, gamma 0.995): **guard stop at it 159** (value loss 42,913 vs 493),
  the fastest runaway yet (highest lr and gamma of the night). Training: falls 0 -> 55% -> 31%, hits 0.01 -> 0.11/s, head share
  22 -> 59%. Exam (it 150, `book_n3t`): footing PASS 0.00, attack 0.05/s at 1.4 m/s FAIL (v3c1 0.11 at 3.3), under pressure 0.00,
  guard PASS 50%, chin PASS, getting up 28/30 PASS, carrying on 24/28 FAIL (v3c1 55/59). Not better than the shipped v3c1.
  Queued the clean A/B: **`v3c4_trump` = the same command + `--no-cut-bootstrap`** (1.5 h) + exam on `book_n4t`.
- **00:33 `v3m4_kim` (`--no-cut-bootstrap`, Huber 10, fresh critic, 1.5 h, ran the full 886 iterations):** the critic
  stayed sane for the whole run: value loss 50-60 from iteration 25 to 885, no trend and no guard stop. Every critic
  that bootstrapped at cuts this week blew up between iterations 150 and 410 (v3m2 x2, v3m4_grandma, v3c3_trump, and
  v3p2_kim on 11-01). This is the first strong evidence that the time-out/survivor-cut bootstrap causes the divergence.
  Trump's A/B (`v3c4_trump`) will confirm it on one boxer. Training falls went from 0.44 to 0.20-0.24. Exam (book_n4k)
  against the shipped `v3m3_kim` (exam_base_1113):
  footing 1.33 vs 3.33 falls a minute (better, still FAIL: mark 0.2), attack 0.35/s at 2.9 m/s vs 0.44 at 3.6 (worse),
  under pressure 0.00 vs Zombie (0.02 before), guard 61% vs 72% (PASS both), chin PASS, getting up 30/30 in 1.9 s,
  carrying on 24/30 vs 26/30. Verdict: kept on file, not proposed; she trades punch for footing. The critic result is
  what matters here.
- **01:35 `v3m5_grandpa` (from v3m4_grandpa it 400, fresh critic, `--no-cut-bootstrap`, Huber 10, 1 h, 501 iterations, no guard
  stop):** critic sane again: value loss 47 at iteration 40, settling to 34-37 from iteration 200 to 500. Training falls
  0.62 -> 0.28. Exam (book_n5p) against v3m4_grandpa: footing **1.33** vs 4.00 falls a minute (baseline rescaled v2: 2.7;
  best Grandpa yet, still FAIL), attack 0.01/s at 0.4 m/s (still about nothing), guard 69% vs 64% (0.77 blocks/s), chin
  0.00 legs/min, getting up 29/30 in 3.0 s, carrying on 24/29 vs 25/30. Verdict: the best Grandpa on footing and guard,
  proposed to the owner as a candidate (not promoted). He still does not punch; that is a reward problem, not a critic one.
- **01:40 queued `v3m5_grandma`:** the v3m4_grandma command (from v3m3_grandma, fall penalty 20) plus `--reset-critic`
  (v3m4 kept the critic and had diverged by iteration 274) and `--no-cut-bootstrap`, 1.5 h, exam on book_n5g. Grandma got the
  flag because her two earlier runs were cut short by the divergence before they could show anything.
- **03:08 A/B result, `v3c4_trump` = exactly the `v3c3_trump` command + `--no-cut-bootstrap`:** v3c3 (bootstrap on) value
  loss 391 -> 940 (it 120) -> 42,913 and guard stop at iteration 159. v3c4 (flag) ran the full 1.5 h, 938 iterations, no
  guard stop: value loss 300-550 to iteration 760, then 600-730 to the end. That is a slow rise (about x1.7 over 900
  iterations) as the hit rate grew 0.11 -> 0.19/s and returns with it, not the geometric runaway. **Confirmed: bootstrapping
  V(s_t) at time-outs and survivor cuts makes this critic diverge.** Same result on Kim and Grandpa (3 of 3 runs sane with
  the flag; 6 of 6 recent runs without it diverged). The proper fix is to bootstrap from the true next state (needs
  `envs/boxing.py` to observe before reset); until then `--no-cut-bootstrap` is the safe setting. Making it the default is
  the owner's call. Exam (book_n4t) against shipped v3c1: attack 0.11/s at 2.0 m/s (0.11 at 2.3), footing 1.33 falls a minute
  (FAIL; 0.00), guard 46% (63%), carrying on 24/28 (27/29). Not better as a boxer; kept on file, not proposed.
- **03:10 queued `v3m6_grandpa`** (last training slot, about 04:40-05:45): from v3m5_grandpa (his best footing), the same
  recipe with the flag and a fresh critic, `--hit-w` 1.5 -> 3.0 and `--seek-w` 0.3 -> 0.6, 1 h. Tests whether he starts
  punching now that he stays up. Exam on book_n6p.
- **04:40 `v3m5_grandma` (from v3m3_grandma, fresh critic, `--no-cut-bootstrap`, Huber 10, fall penalty 20, 1.5 h, 761
  iterations, no guard stop):** critic sane (value loss 59 at iteration 50, falling to 24-31 from iteration 250 on; v3m4_grandma
  diverged at 274). That makes it 4 of 4 runs sane with the flag. Training: falls share 0.86 -> 0.43-0.54 (half the episodes start from
  a hand-over), carry_on 0.20 -> 0.39-0.44. Exam (book_n5g) against v3m3_grandma: **footing PASS, 0.00 falls a minute against
  all seven** (6.67 before; the rescaled v2 baseline was 11.3; also 0 dazed falls except 1.33/min against Zombie), guard 29% vs
  36% (PASS), chin PASS, getting up 27/30 in 4.2 s, carrying on 10/27 vs 13/26 (FAIL), attack 0.00 (as before). Verdict:
  Grandma's first footing pass in a bout and her best boxing brain; proposed to the owner as a candidate (not promoted).
  She does not punch at all, and the first seconds after a get-up are her weak spot. Next for her: the punch-reward recipe
  if v3m6_grandpa shows it works.
- **05:42 `v3m6_grandpa` (from v3m5_grandpa, fresh critic, flag, `--hit-w` 3.0, `--seek-w` 0.6, 1 h, 503 iterations, no guard
  stop):** critic sane (value loss 35-47; 5 of 5 runs with the flag). Exam (book_n6p) against v3m5_grandpa: **footing PASS 0.00
  falls a minute** (1.33), guard 66% (PASS), chin PASS, getting up 30/30 in 2.5 s, carrying on 26/30 (24/29), attack 0.01/s
  at 0.8 m/s (unchanged). Verdict: Grandpa's best brain (footing, get-up and carry-on all at their best), proposed as a
  candidate, not promoted. **Doubling the hit reward did not make him punch:** training hits stayed at 0.02-0.04/s the whole
  run. Punch speed is 0.4-0.8 m/s against a mark of 3.0, so the weight is not the limit: with almost no landed hits there is
  nothing for the reward to reinforce. Suspects for the next session: the muscle speed limit and fatigue on the old bodies
  capping fist speed, and the hit counter's minimum speed. Check what fist speed the grandparents' arms can reach at all
  (a scripted max-effort jab in C MuJoCo) before training more for punching. No Grandma punch run queued for that reason.
- **05:45 GPU left idle** for the last 45 minutes: no job worth starting would finish and be examined before 06:30.

## 2026-11-16 10:05, ten more hours (to about 20:00)

- **10:07 `v3m6_grandma` queued and started:** from v3m5_grandma (her footing pass), same recipe with the flag and a fresh
  critic, `--handover-share` 0.5 -> 0.7 and `--recover-w` 1.0 -> 2.0, 1.5 h. Aimed at her weak spot: carrying on 10/27.
  Exam on book_n6g.
- **Why the grandparents don't punch, part 1: it is not the body.** New `tools/fist_speed.py` (C MuJoCo 3.5.0, pelvis held,
  speed-limit damping on, right arm at every full-effort combination for 0.4 s, `logs/fist_speed.json`): peak glove speed
  Matt 13.9, Kim 12.6, Trump 12.2, **Grandma 9.0, Grandpa 9.0 m/s**. Their arms have half the force and 3/4 the speed
  limits, but they can still reach three times the 3.0 m/s mark (a hit counts above 1 m/s).
- **Part 2: the recipe.** Their match recipe carries `--arms-w 0.01` (a pull of the shoulders and elbows to the guard) and
  `--slow-v 4.0` (a punch slower than 4 m/s pays (v/4)^2: a first clumsy 1.5 m/s punch earns 14%). Kim's recipe, which
  punches, has neither. Hypothesis: together they leave no gradient towards punching for a weak, slow arm.
- **10:20 queued `v3m7_grandpa`:** from v3m6_grandpa (footing pass), same recipe without `--arms-w` and `--slow-v`, `--hit-w`
  2.0, flag, fresh critic, 1.5 h. Exam on book_n7p.

## 2026-11-16 15:35, Timmy, the ninth boxer: v3 body and the first queue (owner: "add timmy ... train and give unique fighting style")
- Mesh: the owner's `TIMMY_unity_rigged.fbx` (AccuRig). Staged in `BLENDER/2026-11-16_timmy/stage.py`: its four clips
  posed it into spikes (imported without them); in Blender the mesh did not sit on its skeleton (stood up while the bones
  lay along -Y, and lower), so it was moved onto them by a similarity fit of each bone's centre to the weighted centre of
  the vertices it drives (6 bones, residual 0.74 -> 0.034 m); legs straightened from 27 degrees apart; 1.76 m. A test pose
  deforms cleanly. `Assets/Models/Timmy.glb`.
- Body: `build_roster.py --only timmy` (73.1 kg, pelvis 0.877 m, no self-contact in 25 poses after one trunk slimming),
  `build_v3.py --only timmy` (head 0.113 m from a half-width of 0.119, ankle -42/208 N m).
- Style, persona **infighter** ("body-snatcher"), the roster's gap: range_m 0.56 (the shortest), body 2.0, head 0.7,
  hand_l 1.3, power 1.5, pace 0.45, press 0.8, crowd 0, taken 0.7, counter 1.2, energy 0.9.
- Warm starts: Sokol's shipped v3 brains (the nearest build, 1.83 m 83 kg), his per-joint action scale written into the
  ONNX checkpoints and rescaled onto Timmy (`tools/warm_from_boxer.py`; ratio 0.991-1.000, targets moved under 2e-6 rad).
- Queue `logs/queue_timmy.json` (its own state; the 58-job `logs/queue.json` of another session left alone; its runner had
  died after starting v3m6_grandma at 10:07): v3f_timmy (footwork, 0.5 h) + exam, v3u_timmy (get-up against a frozen Matt,
  1 h, done at 0.85) + hand-over bank, v3m_timmy (gauntlet against the eight shipped match brains, 1.5 h, the newest
  recipe's terms) + exam on `logs/book_v3_timmy.json`. About 3.5 h of GPU.

## 2026-11-16 15:50, Soldat, the tenth boxer, queued behind Timmy (owner: "also import soldat. rig him with blender / train him")
- Mesh: `soldat_unity.fbx`, one mesh with no skeleton and no weights (A-pose). Rigged in `BLENDER/2026-11-16_soldat/rig.py`:
  joints found from the mesh (leg and arm slices, the fingertip, the deltoid's edge), Mixamo names, Blender heat weights
  (0 vertices left bare), 1.84 m. Two corrections from the build's own check: the crotch detector found 0.34 of height
  (trouser legs touching), so hip and knee are set by proportion (0.48, 0.285); and the pelvis joint level with the hips
  made the trunk slim 8 times (to 58% of the body) to clear the thighs: the pelvis joint 0.015 and the spine root 0.075
  above the hips, as every other rig has them, and it builds with no slimming (trunk 0.128 m, 90%). Test pose clean.
- Style, persona **marksman** ("the sniper"): hand_r 1.6 (no one else is paid for the rear hand), range_m 0.84, head 1.7,
  body 0.6, power 1.4, pace 0.6, counter 1.3, crowd 0.4; Grandpa's lesson kept (taken 1.0, press 0.3).
- Warm starts: Matt's shipped v3 brains (1.86 m 85 kg; ratios 1.000-1.032). Jobs v3f/v3u/v3m_soldat + exams appended to
  `logs/queue_timmy.json`; his gauntlet includes Timmy's new match brain.
- Also today: the phone-layout tests' free-for-alls were being rated (FreeForAll.rated, new today) and moved the owner's
  ladder by up to 700 points; the tests now switch rating off and the ratings were put back.
- 18:07 (owner: "continue training for next 3 hours"): v3m_soldat cut to 1.0 h so Soldat's whole set ends about 21:15;
  Timmy's game brain is v3m_timmy at about iteration 460 (17:59), promoted uncommitted; Soldat runs Matt's until his own.
- 18:39 **exam_v3m_timmy** (iteration 654, 1.5 h): footing, guard (41%), chin pass; attack 0.46/s at 5.6 m/s (mark 0.5,
  1% to the head: the body-snatcher's style), under pressure 0.04/s v Matt, getting up 41/60, carrying on 17/41 fail.
  Promoted into Assets (uncommitted) in place of the 17:59 snapshot. Soldat's queue started 18:39.

### 2026-11-16 20:00 — round 2 for Timmy and Soldat queued (owner: "training for another 10 hours")
Appended to `training/logs/queue_timmy.json` after Soldat's first set (ends ~21:00), about 8 h, finishing ~05:00:
- `v3u2_timmy` get-up top-up from v3u_timmy (1.5 h, done-when 0.9): his exam got up only 68%. Then `bank_v3b_timmy`.
- `v3m2_timmy` match from v3m_timmy (2 h, new hand-over bank, Soldat added to the gauntlet), `exam_v3m2_timmy` (`book_v3b_timmy.json`).
- `v3u2_soldat` (1 h), `bank_v3b_soldat`, `v3m2_soldat` from v3m_soldat (2.5 h, faces v3m2_timmy), `exam_v3m2_soldat`.
Nothing is promoted automatically; promote after comparing the exams against round 1.

### 2026-11-17 — round 2 results, promoted
Exam (exam_v2, 60 trials, 12 rounds), round 1 -> round 2:
- **Timmy 3/7 -> 6/7.** Attack 0.46 -> 0.54/s (pass), getting up 41/60 -> 60/60 in 2.2 s, carrying on 17/41 -> 57/60, chin worst 0.44 -> 0.
  Still failing: under pressure, 0.02/s against Matt (mark 0.25).
- **Soldat 3/7 -> 5/7.** Getting up 31/60 -> 57/60, carrying on 22/31 -> 55/57, guard 34% -> 59%, now 38% to the head (his marksman style).
  Still failing: attack 0.10/s at 2.0 m/s (marks 0.5, 4.0), under pressure 0 against Zombie.
- Promoted (uncommitted): `PromoteBoxing` timmy v3m2/v3u2, soldat v3m2/v3u2 (get-up ONNX via `tools/export_getup_v2.py`), and Soldat's own
  footwork `Promote("soldat","v3f_soldat")` (stand 100%, walk pass, turn 83% vs mark 90%) replacing Matt's.
- Timmy and Soldat were missing from `Testbed.unity`'s roster ("the model has no a_root" in MjGateTests); added. MjGateTests 30/30.

## 2026-11-23 00:10, training as needed, open-ended (owner: "search for what needs training and train indefinitely as needed")
- PC: RTX 5070 Ti Laptop (12 GB). No checkpoints on disk: the ten shipped brains rebuilt with `tools/onnx_to_pt.py` into
  `Training/checkpoints/shipped/` (`match_` with obs_state as trained, `match0_` as the game feeds it, `getup_`, `policy_`).
  Note: `uv run --group train` re-syncs torch to the CPU build; use `uv run --no-sync` after installing the CUDA build.
- **Baseline exam (exam_v2, models/v3, `--getup-no-drain`, all ten, `logs/exams/base_{game,state}_*.json`), game feed:**
  Matt 4/7, Zombie 5/7, Nick 4/7, Trump 3/7, Grandma 3/7, Grandpa 3/7, Kim 3/7, Sokol 4/7, Timmy 4/7, Soldat 4/7.
  Footing collapsed for the old roster **against Timmy and Soldat** (never in their gauntlets): Trump v Soldat 8.7 own +
  19.3 hit falls a minute, Grandma v Timmy 12.3, Grandpa v Timmy 7.7, Kim v Timmy 3.0. Under pressure: 0 of 10.
- **obs_state gap:** every v3 promotion (11-02 to 11-13) dropped `"obs_state": true` from the boxer configs while the v3
  recipe trains with `--obs-state`, so the game feeds zeros (MjBoxer.cs line 538). Measured: state fed vs zeros differs
  within exam noise (e.g. Matt attack 0.48 v 0.33/s, Sokol 5/7 v 4/7, Trump 3/7 both). Not fixed; owner's call.
- **Timmy/Soldat rules gap, fixed:** their configs (models/v3 and the game's copy) had no `speed_limit`, `fatigue_j`,
  `hit_push`, `stagger_v`, `hurt_full`, `body_drain`: trained under default rules + the recipe's punch rules, the game ran
  them with no speed damping, no fatigue and no hit push. Written in (Matt's values, fatigue 20000 J = DEFAULT_FATIGUE_J).
  MjParityTests / MjGateTests still to run in Unity for the two.
- **Round v4 queued** (`logs/make_queue_v4.py`): a gauntlet against all nine others (Timmy and Soldat weighted up),
  `--reset-critic --no-cut-bootstrap --value-huber 10`, 1.25 h each, then the exam: Trump, Grandpa, Grandma, Kim.
- **00:28 `v4m_trump` faulted** (CUDA 719, "unspecified launch failure", GPU at 82-86 C: the laptop driver fault of
  2026-10-02) at iteration 130, save at 125. `tools/train_resilient.py --gauntlet` now wraps every queued run (restart from
  the last save; it now also drops `--reset-critic` on a restart, which would otherwise throw away the critic each time).
  Lengths are given as `--more-iters` (about 9.6 iterations a minute; 700 = about 1.2 h), `--max-hours 0`.
  Trump carries on as `v4m2_trump` from the save at 130, critic kept.
- 00:35 and 00:53: two more faults (`v4m2_trump`), each logged by Windows as nvlddmkm event 13 then 153 (the driver resets
  the GPU, which also drives the display). Three faults in 40 min at 4608 rings, 78-86 C. Queue rebuilt at **3072 rings**;
  Trump carries on as `v4m3_trump` from v4m2's save (about 250 iterations in all so far), 450 more.
- **01:45 `v4m3_trump` done** (about 450 iterations from shipped, 3072 rings; two more faults at 01:39/01:41, picked up by
  the wrapper). Exam (`logs/exams/v4m3_trump.json`) v shipped: **footing 2.33 v 8.67 falls a minute** (v Soldat 2.33,
  was 8.67 own + 19.3 from hits), attack 0.14/s at 1.8 m/s (0.16 at 2.9), guard 47% pass, chin pass, getting up 29/30,
  carrying on 25/29 (23/28), under pressure 0.00. Still 3/7; footing is the gain. Not promoted. Queued `v4m4_trump` (about
  700 more from it) after Kim. Exams now run beside the queue (CPU), not as queue jobs that leave the GPU idle 20 min.
  (A change to `train_resilient.py`'s iteration count was made and taken back: train_gauntlet.py starts a new run name at
  iteration 0 whatever save it resumes, so the wrapper's total-from-0 count was right.) Restarts now wait 180 s to cool.
- **03:05 `v4m_grandpa` done** (700 iterations from shipped, 3072 rings, three faults picked up by the wrapper). Exam v
  shipped: **footing PASS 0.00 falls a minute (7.67; v Timmy 7.67 own + 8.67 hit before)**, guard 26% pass, chin pass (worst
  2.00 legs/min v Nick), getting up 29/30 in 2.8 s, carrying on 22/29 (22/29), attack 0.04/s (0.06), under pressure 0.00.
  **4/7, was 3/7, no line worse: a candidate for the game** (not promoted; promotion needs the editor: PromoteBoxing +
  MjParityTests/MjGateTests). Still no punch: the old note (11-16) stands, it is the recipe, not the arm.
- **04:28 `v4m_grandma` done** (700 iterations, two faults). Exam v shipped: footing 4.00 v 12.33 falls a minute (v Kim,
  Soldat, Trump now 0; left: Timmy 4.00, Nick 3.00), attack 0.00, guard 46% pass, chin pass, getting up 26/30 in 4.0 s,
  carrying on 8/26 (10/26). 3/7 as before; not a candidate. Queued `v4m2_grandma` from it (critic kept, 700 more, weights
  timmy=4 nick=3 zombie=2 trump=2) after v4m4_trump.
- **05:50 `v4m_kim` done** (700 iterations). Exam v shipped: footing 2.00 v 3.00 (v Timmy 0.67 v 3.00, v Sokol 2.00 v
  1.67), but **attack 0.12/s v 0.41/s** (v Nick 0.20 v 1.98), carrying on 24/30 (25/30). Worse as a boxer: shipped Kim
  stays; the gauntlet trades her punch for footing (as v3m4_kim did on 11-15). Not repeated.
- Queued `v4m_soldat` after v4m2_grandma: Soldat's attack (0.10/s) with `--hit-w 2.0 --seek-w 0.3`, weights zombie, sokol,
  matt = 2 (his hardest), fresh critic, 700 iterations.
- **07:09 `v4m4_trump` done** (700 more from v4m3, no faults). Exam: footing 1.00 falls a minute (v4m3 2.33, shipped 8.67;
  all v Soldat), attack 0.10/s at 1.6 m/s (0.14, 0.16), carrying on **18/30** (25/29, 23/28), guard 48%, chin, getting up
  30/30. Mixed: every round makes him more defensive and slower-handed. **Of the Trump brains, `v4m3_trump` is the
  better candidate** (footing 2.33, carrying on 25/29); still 3/7, not proposed. No more Trump gauntlets on this recipe:
  his punch (shortest reach, 0.44 m) needs a different lesson.
- 07:30 hand-over banks `logs/handover_v4_{grandpa,grandma,trump,nick,sokol,kim}.npz` (make_handover_v2, models/v3, the
  shipped get-up brains, 1500 states each). Queued carry-on runs `v4r_*` (11-02 recipe: `--handover-share 0.5
  --blend-steps 15 --recover-w 1.0 --handover-reserve 0.5`, 500 iterations; all four configs already blend 15):
  Grandpa from v4m_grandpa (critic kept), Sokol and Nick from shipped (fresh critic), Trump from v4m3_trump.
- **08:52 `v4m2_grandma` done** (700 more from v4m_grandma, five faults). Exam: footing 3.00 (v Timmy 0, v Nick 3.00),
  **guard FAIL 23%** (46%), carrying on 14/26 (8/26; shipped 10/26), attack 0.00, chin and getting up pass. 2/7: not a
  candidate. Grandma's gauntlets stop here: footing moves, nothing else, and never a punch (tasks.md V1: her body,
  strength 0.53 / speed 0.75, is the owner's call). Faults are getting closer together (five in 1.7 h, 77-86 C).
- **09:26 `v4m_soldat` stopped at iteration 250** (of 700): training hits 0.00-0.03/s at 0.6-1.5 m/s from the first
  iteration on; `--hit-w 2.0 --seek-w 0.3` gives nothing to reinforce when he almost never lands (Grandpa's lesson of
  11-16 again). Soldat's punch needs a different lesson (a scripted punch to imitate, or a reward for fist speed
  towards the target before contact), not more hit weight. Carry-on runs `v4r_*` started at 09:26 with Grandpa.
- **10:00 Grandpa promoted into Assets (uncommitted, tests pending):** `v4m_grandpa` exported to ONNX (same 1x103 input,
  `actions`/`value` outputs as shipped), rebuilt to a checkpoint with obs_state off and examined as the game feeds it
  (`logs/exams/promote_grandpa_game.json`): footing PASS 0.00, guard 52%, chin, getting up 29/30, carrying on 22/29, attack
  0.03/s: 4/7, as in training. Copied over `Assets/_Project/RL_Models/Grandpa/match.onnx` (.meta kept, so the prefab's
  reference holds; the shipped file is kept outside the repo). **MjParityTests / MjGateTests not run:** Unity on this PC
  stops at "No valid Unity Editor license found" (editor and batchmode alike) until the license is activated in Unity Hub.
- 10:40 (owner: "finish up in 5 mins"): Unity licensed (Personal) and opened; `Assets/_Project/Core/Scripts/Editor/DevBridge.cs`
  (a file-polled command bridge in place of the missing Unity CLI) compiled with 0 warnings; its first capture script did not
  run (a line-count bug, fixed). Editor closed; ProjectSettings churn (a Sentis WebGL define) reverted. Not done: the capture
  report and MjParityTests/MjGateTests for Grandpa's promoted brain. Queue resumed: v4r_sokol, v4r_nick, v4r_trump.
