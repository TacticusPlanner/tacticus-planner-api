"""Round-5 catalog transform (runs on the post-round-4 Data tree).

Two follow-up fixes to the campaign-battles / drop-chances datasets:

1. Make each group's `faction` correspond to the PLAYABLE faction, i.e. the faction of its
   `coreCharacters`. Round-4 left two event groups carrying the enemy faction
   (`necrons-vs-dark-angels` had `DarkAngels`, `world-eaters-vs-adepta-sororitas` had
   `Sisterhood`); both now become the core-characters' faction (`Necrons` / `WorldEaters`).
   Character->factionId comes from the units datasets.

2. Drop the numerator/denominator from `chanceId` (they are rates that can be rebalanced and
   should not be baked into a stable id). New `chanceId` = `{rewardKind}_{difficulty}` and is
   unique once the conflated `eventChallenge` difficulty is split: every event has both a
   "Standard Challenge" and an "Extremis Challenge" campaign track that round-4 both normalized
   to `eventChallenge` (with different drop rates). They are re-tagged
   `eventStandardChallenge` / `eventExtremisChallenge`, recovered from the original campaign
   names in git HEAD. drop-chances.json keeps its numerator/denominator/effectiveRate columns
   (the live rate data) but is re-keyed by the new stable id.

3. schemaVersion bumped 5 -> 6.
"""
import json
import os
import glob
import subprocess

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
CB_DIR = os.path.join(DATA, "campaign-battles")


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


def git_head_json(rel):
    out = subprocess.run(
        ["git", "show", f"HEAD:{rel}"], cwd=API, capture_output=True, text=True, encoding="utf-8"
    )
    if out.returncode != 0:
        raise RuntimeError(out.stderr)
    return json.loads(out.stdout)


# ---- character id -> factionId (playable faction) --------------------------------------------
char_faction = {}
for path in glob.glob(os.path.join(DATA, "units", "*.json")):
    f = load(path)
    for c in f["characters"]:
        char_faction[c["id"]] = f["factionId"]


# ---- battle id -> refined difficulty (split eventChallenge by parent tier) --------------------
orig_campaigns = (
    git_head_json("src/TacticusPlanner.Catalog/Data/campaigns.json")
    + git_head_json("src/TacticusPlanner.Catalog/Data/campaign-events.json")
)
campaign_name = {c["id"]: c["name"] for c in orig_campaigns}
battle_campaign = {
    b["id"]: b["campaign"]
    for b in git_head_json("src/TacticusPlanner.Catalog/Data/campaign-battles.json")
}


def refine_difficulty(battle_id, current):
    if current != "eventChallenge":
        return current
    name = campaign_name[battle_campaign[battle_id]]
    if "Standard Challenge" in name:
        return "eventStandardChallenge"
    if "Extremis Challenge" in name:
        return "eventExtremisChallenge"
    raise KeyError(f"cannot refine eventChallenge for {battle_id} (campaign '{name}')")


# ---- old chanceId -> rewardKind / numerator / denominator / effectiveRate ---------------------
old_chances = {c["id"]: c for c in load(os.path.join(DATA, "drop-chances.json"))}


def distinct(seq):
    seen, out = set(), []
    for x in seq:
        if x not in seen:
            seen.add(x)
            out.append(x)
    return out


new_chances = {}
battle_total = 0
for path in sorted(glob.glob(os.path.join(CB_DIR, "campaign-battles-*.json"))):
    g = load(path)

    core_factions = distinct(char_faction[cid] for cid in g["coreCharacters"])
    assert len(core_factions) == 1, (g["groupId"], core_factions)
    playable_faction = core_factions[0]

    new_battles = []
    for b in g["battles"]:
        battle_total += 1
        difficulty = refine_difficulty(b["id"], b["difficulty"])

        potential = []
        for p in b["rewards"]["potential"]:
            old = old_chances[p["chanceId"]]
            cid = f"{old['rewardKind']}_{difficulty}"
            entry = {
                "id": cid,
                "rewardKind": old["rewardKind"],
                "difficulty": difficulty,
                "numerator": old["numerator"],
                "denominator": old["denominator"],
                "effectiveRate": old["effectiveRate"],
            }
            if cid in new_chances:
                assert new_chances[cid] == entry, (new_chances[cid], entry)
            else:
                new_chances[cid] = entry
            potential.append({"id": p["id"], "chanceId": cid})

        nb = {}
        for k, v in b.items():
            if k == "difficulty":
                nb["difficulty"] = difficulty
            elif k == "rewards":
                nb["rewards"] = {"guaranteed": v["guaranteed"], "potential": potential}
            else:
                nb[k] = v
        new_battles.append(nb)

    write(path, {
        "groupId": g["groupId"],
        "faction": playable_faction,
        "releaseType": g["releaseType"],
        "coreCharacters": g["coreCharacters"],
        "difficulties": distinct(b["difficulty"] for b in new_battles),
        "battles": new_battles,
    })

write(os.path.join(DATA, "drop-chances.json"), [new_chances[k] for k in sorted(new_chances)])
assert battle_total == 1316, battle_total


# ---- regenerate manifest (schemaVersion 6) ---------------------------------------------------
def datasets_in(subdir):
    return [
        {"key": os.path.basename(p)[: -len(".json")], "file": f"{subdir}/{os.path.basename(p)}"}
        for p in sorted(glob.glob(os.path.join(DATA, subdir, "*.json")))
    ]


datasets = []
datasets += datasets_in("units")
datasets.append({"key": "mow-upgrade-costs", "file": "mow-upgrade-costs.json"})
datasets.append({"key": "drop-chances", "file": "drop-chances.json"})
datasets += datasets_in("npcs")
datasets += datasets_in("equipment")
datasets += datasets_in("upgrades")
datasets += datasets_in("campaign-battles")
datasets += datasets_in("lres")

write(os.path.join(DATA, "catalog-manifest.json"),
      {"version": "dev-2026-06-25", "schemaVersion": 6, "datasets": datasets})

print("drop-chances:", len(new_chances), "battles:", battle_total, "datasets:", len(datasets))
print("difficulties:", sorted({c["difficulty"] for c in new_chances.values()}))
