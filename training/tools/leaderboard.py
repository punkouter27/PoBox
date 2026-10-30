"""An Elo for every fighter from the bouts people watched: the online leaderboard's table, or fight logs copied off phones.

    python tools/leaderboard.py --log path/to/fightlog.ndjson [more.ndjson ...]
    python tools/leaderboard.py --table "https://ACCOUNT.table.core.windows.net/pobox?sv=...&sp=r&sig=..."
    python tools/leaderboard.py --log a.ndjson --out ../Assets/WebGLTemplates/Po/leaderboard.json

The game writes every finished bout to persistentDataPath/fightlog.ndjson (League/FightLog.cs) and, with the settings'
BOARD set, posts the same bout to an Azure Table (Online/Leaderboard.cs). This reads either, and rates the fighters
the way tools/elo.py rates the CPU's bouts: a Bradley-Terry fit of every result on the Elo scale (400 points = 10 to
1), centred on 1000, a draw half a win each, so the order the bouts were watched in does not matter. The ladder on
one phone (LeagueTable) moves bout by bout from the CPU's ratings; this one pools every device.

--out writes the table as JSON (what Assets/WebGLTemplates/Po/leaderboard.html shows when it is given no table to read).
Standard library only: runs on any Python 3.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import urllib.parse
import urllib.request


def bouts_from_log(path: str) -> list[dict]:
    """The fight log's lines as bouts: red, blue, winner ('' a draw), knockout, when."""
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            out.append({"red": r["red"]["name"], "blue": r["blue"]["name"], "winner": r.get("winner", ""),
                        "knockout": bool(r.get("knockout")), "when": r.get("when", ""), "device": "log"})
    return out


def bouts_from_table(url: str) -> list[dict]:
    """Every row of the Azure Table (a SAS URL that may read), following the continuation headers."""
    out, extra = [], {}
    base = urllib.parse.urlsplit(url)
    while True:
        query = base.query + ("&" + urllib.parse.urlencode(extra) if extra else "")
        req = urllib.request.Request(urllib.parse.urlunsplit(base._replace(query=query)),
                                     headers={"Accept": "application/json;odata=nometadata", "x-ms-version": "2019-02-02"})
        with urllib.request.urlopen(req, timeout=30) as resp:
            data = json.load(resp)
            np_, nr = resp.headers.get("x-ms-continuation-NextPartitionKey"), resp.headers.get("x-ms-continuation-NextRowKey")
        for r in data.get("value", []):
            out.append({"red": r["Red"], "blue": r["Blue"], "winner": r.get("Winner", ""), "knockout": bool(r.get("Knockout")),
                        "when": r.get("When", ""), "device": r.get("Device", "")})
        if not np_:
            return out
        extra = {"NextPartitionKey": np_, "NextRowKey": nr or ""}


def bradley_terry(names: list[str], bouts: list[dict], iters: int = 2000) -> dict[str, float]:
    """Strengths by the minorise-maximise fit (Hunter 2004), as tools/elo.py does it: every fighter is given half a draw
    against a 1000 ghost, so one who has won (or lost) everything stays finite. On the Elo scale, mean 1000."""
    k = {n: i for i, n in enumerate(names)}
    n = len(names)
    wins = [0.5] * n
    games = [[0.0] * n for _ in range(n)]
    for b in bouts:
        a, c = k[b["red"]], k[b["blue"]]
        s = 1.0 if b["winner"] == b["red"] else 0.0 if b["winner"] == b["blue"] else 0.5
        wins[a] += s
        wins[c] += 1.0 - s
        games[a][c] += 1
        games[c][a] += 1
    p = [1.0] * n
    for _ in range(iters):
        p = [wins[i] / (sum(games[i][j] / (p[i] + p[j]) for j in range(n)) + 1.0 / (p[i] + 1.0)) for i in range(n)]
        g = math.exp(sum(math.log(x) for x in p) / n)
        p = [x / g for x in p]
    elo = [400.0 * math.log10(x) for x in p]
    mean = sum(elo) / n
    return {nm: 1000.0 + elo[i] - mean for nm, i in k.items()}


def table(bouts: list[dict]) -> list[dict]:
    names = sorted({b["red"] for b in bouts} | {b["blue"] for b in bouts})
    if not names:
        return []
    elo = bradley_terry(names, bouts)
    rows = {n: {"name": n, "elo": round(elo[n], 1), "wins": 0, "losses": 0, "draws": 0, "knockouts": 0, "bouts": 0} for n in names}
    for b in bouts:
        for me, other in ((b["red"], b["blue"]), (b["blue"], b["red"])):
            r = rows[me]
            r["bouts"] += 1
            if b["winner"] == me:
                r["wins"] += 1
                r["knockouts"] += int(b["knockout"])
            elif b["winner"] == other:
                r["losses"] += 1
            else:
                r["draws"] += 1
    return sorted(rows.values(), key=lambda r: -r["elo"])


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--log", nargs="*", default=[], help="fightlog.ndjson files")
    ap.add_argument("--table", default="", help="the Azure Table's URL with a SAS token that may read (sp=r)")
    ap.add_argument("--out", default="", help="write the table here as JSON")
    args = ap.parse_args()
    bouts = [b for p in args.log for b in bouts_from_log(p)]
    if args.table:
        bouts += bouts_from_table(args.table)
    if not bouts:
        sys.exit("no bouts: give --log files or --table")
    rows = table(bouts)
    devices = len({b["device"] for b in bouts})
    print(f"{len(bouts)} bouts, {len(rows)} fighters, {devices} source(s)")
    print(f"{'#':>2}  {'FIGHTER':<10} {'ELO':>6}  {'W-L-D':<9} {'KO':>3}")
    for i, r in enumerate(rows, 1):
        print(f"{i:>2}  {r['name']:<10} {r['elo']:>6.0f}  {r['wins']}-{r['losses']}-{r['draws']:<5} {r['knockouts']:>3}")
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump({"bouts": len(bouts), "rows": rows}, f, indent=1)
        print("written", args.out)


if __name__ == "__main__":
    main()
