"""Write a brain's rules of 2026-10-30 (action filter, observation delay) into a boxer's configs under models/v2, the
file MjRetrofit.Promote copies to Assets/Boxers/NAME/config.json and the game's MjBoxer reads (rules_footwork,
rules_match, rules_getup). The trainer reads them back too (envs/boxing.py: a fighter's own rules when the run does not
give them), so an opponent plays and a brain resumes as it was trained. Set them only for a brain trained under them:
the checkpoint's latest_*_policy_config.json says which ("rules_<brain>").

    python tools/set_brain_rules.py --only matt --brain match --action-filter 0.3 --obs-delay 3
    python tools/set_brain_rules.py --only matt --brain match --clear
    python tools/set_brain_rules.py --from-run checkpoints/rz_d3_matt/latest_matt_policy_config.json
"""
import argparse
import glob
import json
import os

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ap = argparse.ArgumentParser()
ap.add_argument("--only", nargs="*", default=[], help="boxers; none = all")
ap.add_argument("--brain", choices=["footwork", "match", "getup"])
ap.add_argument("--action-filter", type=float)
ap.add_argument("--obs-delay", type=int)
ap.add_argument("--obs-delay-max", type=int)
ap.add_argument("--clear", action="store_true", help="take the brain's rules away (no filter, no delay)")
ap.add_argument("--from-run", default="", help="a run's latest_*_policy_config.json: its boxer and rules_<brain>, as trained")
ap.add_argument("--models", default=os.path.join("models", "v2"))
args = ap.parse_args()

if args.from_run:
    with open(args.from_run, "r", encoding="utf-8") as f:
        made = json.load(f)
    keys = [k for k in made if k.startswith("rules_")]
    if not keys:
        raise SystemExit(f"{args.from_run} has no rules_<brain>: it was trained before 2026-10-30 (no filter, no delay)")
    only, brain, rules = [made["name"]], keys[0][len("rules_"):], made[keys[0]]
else:
    if not args.brain:
        raise SystemExit("--brain or --from-run")
    only, brain = args.only, args.brain
    rules = None if args.clear else {k: v for k, v in (("action_filter", args.action_filter), ("obs_delay", args.obs_delay),
                                                      ("obs_delay_max", args.obs_delay_max)) if v is not None}
if rules and not 0.0 <= float(rules.get("action_filter", 0.0)) < 1.0:
    raise SystemExit("action_filter is a share of the last action: 0 <= f < 1")

for path in glob.glob(os.path.join(HERE, args.models, "*_policy_config.json")):
    with open(path, "r", encoding="utf-8") as f:
        cfg = json.load(f)
    mine = [e for e in cfg.get("fighters", [cfg]) if not only or e.get("name") in only]
    for e in mine:
        if rules is None:
            e.pop(f"rules_{brain}", None)
        else:
            e[f"rules_{brain}"] = {**e.get(f"rules_{brain}", {}), **rules}
    if mine:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(cfg, f, indent=2)
        print(f"{os.path.basename(path)}: {', '.join(e.get('name', '?') for e in mine)} rules_{brain} = {rules}")
