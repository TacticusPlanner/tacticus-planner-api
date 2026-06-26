"""Round-4 catalog transform (runs on the post-round-3 Data tree).

Follow-up restructure of the campaign-battles / drop-chances datasets:

1. Drop each battle's `requiredCharacterSnowprintIds` (a same-set duplicate of the group's
   `coreCharacters` in every group except indomitus; the indomitus per-node distinctions are
   intentionally dropped here).
2. Rename each battle's `campaignType` -> `difficulty`, normalized to the group's `difficulties`
   enum (standard/elite/mirror, eventStandard/eventChallenge/eventExtremis). The authoritative
   battle->difficulty mapping is recovered from the pre-transform originals in git HEAD
   (campaigns.json + campaign-events.json + campaign-battles.json), since round-3 dropped the
   per-battle `campaign` link that carried it.
3. Rename the six campaign-EVENT groups (file + groupId) to `<playableFaction>-vs-<enemyFaction>`
   (playable = faction of the coreCharacters; enemy = the existing event groupId). Standard
   campaigns are left unchanged.
4. Enrich `chanceId` to `{rewardKind}_{difficulty}_{numerator}_{denominator}` where rewardKind is
   the upgrade rarity (e.g. upgradeCommon) or shard / mythicShard. drop-chances.json rows gain
   `rewardKind` + `difficulty` columns.
5. Regenerate catalog-manifest.json (schemaVersion 5).
"""
import json
import os
import glob
import subprocess

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
CB_DIR = os.path.join(DATA, "campaign-battles")

# Event group renames: <existing enemy groupId> -> <playable>-vs-<enemy>. Playable slug is the
# canonical hyphenated faction name of the group's coreCharacters; enemy slug stays the old groupId.
EVENT_RENAMES = {
    "adepta-sororitas": "world-eaters-vs-adepta-sororitas",
    "admech": "death-guard-vs-admech",
    "dark-angels": "necrons-vs-dark-angels",
    "death-guard": "adepta-sororitas-vs-death-guard",
    "tau-empire": "genestealers-vs-tau-empire",
    "tyranids": "ultramarines-vs-tyranids",
}


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


# ---- recover authoritative battle -> difficulty from the pre-transform originals -------------
orig_campaigns = (
    git_head_json("src/TacticusPlanner.Catalog/Data/campaigns.json")
    + git_head_json("src/TacticusPlanner.Catalog/Data/campaign-events.json")
)
campaign_difficulty = {c["id"]: c["difficulty"] for c in orig_campaigns}
battle_difficulty = {}
for b in git_head_json("src/TacticusPlanner.Catalog/Data/campaign-battles.json"):
    battle_difficulty[b["id"]] = campaign_difficulty[b["campaign"]]


# ---- upgrade id -> rarity (for rewardKind) ---------------------------------------------------
upgrade_rarity = {}
for path in glob.glob(os.path.join(DATA, "upgrades", "*.json")):
    for u in load(path):
        upgrade_rarity[u["id"]] = u["rarity"]


def reward_kind(reward_id):
    if reward_id.startswith("mythicShards_"):
        return "mythicShard"
    if reward_id.startswith("shards_"):
        return "shard"
    if reward_id.startswith("upg"):
        rarity = upgrade_rarity[reward_id]
        return "upgrade" + rarity[:1].upper() + rarity[1:]
    raise KeyError(f"unclassified potential reward id: {reward_id}")


# ---- current drop ratios keyed by old "num_den" ----------------------------------------------
old_chances = {c["id"]: c for c in load(os.path.join(DATA, "drop-chances.json"))}


# ---- reshape each group + battles ------------------------------------------------------------
def distinct(seq):
    seen, out = set(), []
    for x in seq:
        if x not in seen:
            seen.add(x)
            out.append(x)
    return out


new_chances = {}
battle_total = 0
group_files = sorted(glob.glob(os.path.join(CB_DIR, "campaign-battles-*.json")))
for path in group_files:
    g = load(path)
    old_group_id = g["groupId"]
    new_group_id = EVENT_RENAMES.get(old_group_id, old_group_id)

    new_battles = []
    for b in g["battles"]:
        battle_total += 1
        difficulty = battle_difficulty[b["id"]]

        potential = []
        for p in b["rewards"]["potential"]:
            ratio = old_chances[p["chanceId"]]
            kind = reward_kind(p["id"])
            cid = f"{kind}_{difficulty}_{ratio['numerator']}_{ratio['denominator']}"
            entry = {
                "id": cid,
                "rewardKind": kind,
                "difficulty": difficulty,
                "numerator": ratio["numerator"],
                "denominator": ratio["denominator"],
                "effectiveRate": ratio["effectiveRate"],
            }
            if cid in new_chances:
                assert new_chances[cid] == entry, (new_chances[cid], entry)
            else:
                new_chances[cid] = entry
            potential.append({"id": p["id"], "chanceId": cid})

        nb = {}
        for k, v in b.items():
            if k in ("campaignType", "requiredCharacterSnowprintIds"):
                continue
            if k == "id":
                nb[k] = v
                nb["difficulty"] = difficulty
            elif k == "rewards":
                nb["rewards"] = {"guaranteed": v["guaranteed"], "potential": potential}
            else:
                nb[k] = v
        new_battles.append(nb)

    # difficulties[] from the battles themselves keeps group + battle vocab in lock-step.
    new_difficulties = distinct(b["difficulty"] for b in new_battles)
    assert set(new_difficulties) == set(g["difficulties"]), (new_group_id, new_difficulties, g["difficulties"])

    g_out = {
        "groupId": new_group_id,
        "faction": g["faction"],
        "releaseType": g["releaseType"],
        "coreCharacters": g["coreCharacters"],
        "difficulties": new_difficulties,
        "battles": new_battles,
    }
    if new_group_id != old_group_id:
        os.remove(path)
        write(os.path.join(CB_DIR, f"campaign-battles-{new_group_id}.json"), g_out)
    else:
        write(path, g_out)

write(os.path.join(DATA, "drop-chances.json"), [new_chances[k] for k in sorted(new_chances)])
assert battle_total == 1316, battle_total


# ---- regenerate manifest ---------------------------------------------------------------------
def datasets_in(subdir):
    out = []
    for path in sorted(glob.glob(os.path.join(DATA, subdir, "*.json"))):
        name = os.path.basename(path)
        out.append({"key": name[: -len(".json")], "file": f"{subdir}/{name}"})
    return out


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
      {"version": "dev-2026-06-25", "schemaVersion": 5, "datasets": datasets})

print("drop-chances:", len(new_chances), "battles:", battle_total, "datasets:", len(datasets))
print("renamed groups:", ", ".join(f"{k}->{v}" for k, v in EVENT_RENAMES.items()))
