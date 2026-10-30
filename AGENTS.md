# AGENTS.md — PoBox (UU_AGENT)

Standing directives for any coding agent working in this repo. Project-specific anchors are in *italics*.

> **ML-Agents exception:** ignore the MuJoCo rules if a project uses ML-Agents (`mlagents` / `com.unity.ml-agents`). PoBox does not.

## 1. Workflow, Tooling & Asset Pipeline

- **Git & Docs:** Work on `master` only; other branches only when asked. Commit pending changes before any sync or pull. Check `/DOCS` first.
  *Here: the top of `DOCS/README.md` (running history), `tasks.md` (live plan), `rl_optimization_log.md` (run log), all listed in CLAUDE.md.*
- **Build version:** Show the build version `YYMMDD` (set by the build script from the build date or the git commit date) in the bottom-right corner of every screen.
- **Automation:** Automate scene setup via CLI and headless Blender. Build prefabs and static objects in the scene through the CLI/MCP, not at runtime from code, so their positions can be adjusted in the scene.
  *Here: the Unity CLI against the open editor (or [unity-mcp](https://github.com/CoplayDev/unity-mcp) / [Unity-MCP](https://github.com/IvanMurzak/Unity-MCP), whichever works best); scenes are edited through `eval`, never on disk while open (CLAUDE.md).*
- **Blender edits:** Before editing a Blender scene, archive the original `.blend` in the raw asset path and export a `.glb` copy to the export asset path (older sources stay in `BLENDER/<date>_<name>/`).
- **UI documentation:** Save every UI change as an HTML report with annotated before/after screenshots, changes marked, in `SCREENSHOTS/`.
  *Here: one folder per pass, e.g. `SCREENSHOTS/2026-10-16-ui-integrity/`.*

### Platform setup matrix

| Dimension | Unity | Unreal Engine 5 |
|---|---|---|
| Background processing | Enable Run In Background | Disable Use Less CPU in Background |
| Raw assets | `Assets/Art/Raw/` | `Content/Art/Raw/` |
| Exported assets | `Assets/Art/Exports/GLB/` | `Content/Art/Exports/` |
| Physics engine | MuJoCo (`org.mujoco`) | MuJoCo / URLab |
| AI model runtime | Unity Sentis / ONNX | NNE / ONNX Runtime |

*Here: the Unity column. Also turn on No Throttling (Editor Preferences) and continuous auto-tick, or the unfocused editor crawls. MuJoCo plugin 3.5.0, embedded with `PoBox:` changes; no PhysX in the fight. Inference is the Unity Inference Engine (Sentis). Android builds use [mujoco-bin](https://github.com/joanllobera/mujoco-bin/).*

## 2. Training & Resources

- **Engine & model pipeline:** Train in MuJoCo/Newton. Ask for a skinned mesh before training; validate it, extract its rig hierarchy and import it into MuJoCo.
  *Here: MuJoCo Warp in `training/`; `glb_to_rig.py` → `rig_to_mjcf.py`, roster in `training/roster.json`.*
- **Execution & cleanup:** Close the editor for pure RL runs or runs over 30 min when that speeds training up, and say when it can be reopened. Delete abandoned run folders, dummy checkpoints and TensorBoard logs under 5,000 steps.
  *Here: Unity in play mode cuts Warp from ~60k to ~12k samples/s. One GPU job at a time (check `nvidia-smi`, `train_*.py` processes and `training/logs/queue.log`).*
- **Simulation UI & TensorBoard:** Show the MuJoCo UI during and after training so the movement can be watched. Start TensorBoard as soon as training starts.
  *Here: `training/view_box.py`; `training/tools/viewer_leash.ps1`.*
- **Tracking & logs:** Log training metrics, decisions and updates so later agents can track progress. Send data to Azure Table Storage when needed.
  *Here: `rl_optimization_log.md` at the repo root (run by run) and the dated entries in `DOCS/README.md`.*
- **Telemetry:** Stream agent actions, win/loss stats and kinematics to local files (or Azure Tables) to analyse movement profiles and results over time.
  *Here: `FightLog` writes every bout to `persistentDataPath/fightlog.ndjson`; `PerfTelemetry` for frame cost.*
- **Progress updates:** Estimate training time at startup, then send hourly updates with a status grid (run, iteration, key metrics), key improvements and a short summary.
  *Here: `training/tools/status.ps1 <run>` reads the status file the trainer writes every ten iterations.*

## 3. Physics & Quality Checks

- **Physical fidelity:** Earth gravity (−9.81 m/s²), mass according to size, realistic joint ranges. Human joints move at human speed and force.
- **Muscle rules:** Every new creature trains from the start with a muscle-like speed limit (joint torque falls to nothing at the joint's speed limit) and fatigue (drives weaken as energy is spent and recover with rest). The game runs under the same rules.
- **Collision:** Simple primitive shapes (capsules, boxes, spheres) fitted inside the skinned mesh; never the visual mesh or the bones. All parts collide with each other and the world except parent–child pairs and pairs that overlap in the default stance (joint limits handle those). Clear mesh separation: before training, verify no pair touches in the T-pose, the default stance and a normal arm and leg swing.
  *Here: `training/tools/check_self_collision.py`.*
- **Self-contact:** Never ends an episode. If the policy leans on it, add a small self-contact force penalty.
- **Action smoothing:** Promote human-like, fluid joint motion without jitter: torque/action-rate penalties, low-pass filtering of actions, or jerk minimisation in the reward.
  *Here: every env charges the squared action change (`r["rate"]` in `envs/footwork.py`, `getup.py`, `boxing.py`); `train_box.py --action-rate-w` adds more. No low-pass filter on actions yet: one would have to match in `MjBoxer.Drive`.*
- **Warm start:** Train a new skill from a warm start (a brain trained without self-collision, or the previous rung), not from scratch.
  *Here: rebuild warm starts from shipped brains with `training/tools/onnx_to_pt.py`.*
- **Export gate:** Export ONNX only after the policy reaches the target score for 100 consecutive episodes with zero NaN gradients or errors.
  *Here: then the exams (`footwork_c.py --exam`, `exam_v2.py`) in C MuJoCo 3.5.0 and Unity parity (`MjParityTests`, `MjGateTests`) before `MjRetrofit.Promote`.*
- **Reports:** For runs over 30 min, write an HTML report with the 3 most consequential metric charts, a comparison to previous runs (better or worse, and why), and each chart explained at 3 levels: toddler, child, adult.
  *Here: `training/tools/day_charts.py`, `training/tools/tb_shot.ps1`; reports in `DOCS/reports/`.*

## 4. Communication Rules

- **Large code changes:** If a code change exceeds 100 lines, offer 3 follow-up roadmap/architecture questions.
- **Summary:** End any response longer than 100 words with a ~20-word TL;DR.
- **Problem solving:** End each response with a proposed solution for the biggest problem in the app, and ask for confirmation before going ahead.

## CI/CD and Azure (same section in every Po repo)

- A push to the default branch builds WebGL and deploys it to the game's Azure Static Web App (`.github/workflows/webgl.yml`). It runs on the self-hosted `unity` runner on the dev PC, one Unity build at a time across all games, so the PC must be on. `Tools/deploy_webgl.ps1` stays for a manual deploy, but CI is the normal path.
- The workflow has no `pull_request` trigger, on purpose: the repo is public and the runner is a real PC. Never add one.
- Tests pass on any machine: no drive-letter, temp-folder or working-directory assumptions, and no live config value hard-coded as an expected result (read it from the same config the app reads).
- A red run on the default branch gets an ntfy push from the CI watcher in PoPunkouterSoftware. Fix it, or say why it is red.
