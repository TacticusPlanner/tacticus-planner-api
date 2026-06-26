"""Round-3 catalog transform (runs on the post-round-2 Data tree).

- Slim each campaign-battles-<group>.json: lift faction / releaseType / coreCharacters to the top
  level, replace campaigns[] with a distinct difficulties[] array, drop groupType; drop each battle's
  `campaign`; reference reward chances by chanceId instead of inline numerator/denominator/rate.
- Extract distinct reward chances into a synced drop-chances.json dataset.
- Regenerate catalog-manifest.json (schemaVersion 4).
"""
import json
import os
import glob

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


def chance_id(p):
    return f"{p['chance_numerator']}_{p['chance_denominator']}"


group_files = sorted(glob.glob(os.path.join(DATA, "campaign-battles", "campaign-battles-*.json")))

# ---- collect distinct drop chances -----------------------------------------
chances = {}
for path in group_files:
    for b in load(path)["battles"]:
        for p in b["rewards"]["potential"]:
            if not p.get("id"):
                continue
            cid = chance_id(p)
            entry = {
                "id": cid,
                "numerator": p["chance_numerator"],
                "denominator": p["chance_denominator"],
                "effectiveRate": p["effective_rate"],
            }
            if cid in chances:
                assert chances[cid] == entry, (chances[cid], entry)
            else:
                chances[cid] = entry

write(os.path.join(DATA, "drop-chances.json"), [chances[k] for k in sorted(chances)])


# ---- reshape each group + battles ------------------------------------------
def distinct(seq):
    seen, out = set(), []
    for x in seq:
        if x not in seen:
            seen.add(x)
            out.append(x)
    return out


battle_total = 0
referenced_chance_ids = set()
for path in group_files:
    g = load(path)
    camps = g["campaigns"]
    factions = {c["faction"] for c in camps}
    cores = {tuple(c["coreCharacters"]) for c in camps}
    releases = {c["releaseType"] for c in camps}
    assert len(factions) == 1 and len(cores) == 1 and len(releases) == 1, g["groupId"]

    new_battles = []
    for b in g["battles"]:
        battle_total += 1
        potential = []
        for p in b["rewards"]["potential"]:
            if not p.get("id"):
                continue
            cid = chance_id(p)
            referenced_chance_ids.add(cid)
            potential.append({"id": p["id"], "chanceId": cid})
        b = {k: v for k, v in b.items() if k != "campaign"}
        b["rewards"] = {"guaranteed": b["rewards"]["guaranteed"], "potential": potential}
        new_battles.append(b)

    write(path, {
        "groupId": g["groupId"],
        "faction": next(iter(factions)),
        "releaseType": next(iter(releases)),
        "coreCharacters": list(next(iter(cores))),
        "difficulties": distinct(c["difficulty"] for c in camps),
        "battles": new_battles,
    })

assert battle_total == 1316, battle_total
assert referenced_chance_ids <= set(chances), referenced_chance_ids - set(chances)


# ---- regenerate manifest ----------------------------------------------------
def datasets_in(subdir):
    out = []
    for path in sorted(glob.glob(os.path.join(DATA, subdir, "*.json"))):
        name = os.path.basename(path)
        out.append({"key": name[:-len(".json")], "file": f"{subdir}/{name}"})
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
      {"version": "dev-2026-06-24", "schemaVersion": 4, "datasets": datasets})

print("drop-chances:", len(chances), "referenced:", len(referenced_chance_ids))
print("battles:", battle_total, "datasets:", len(datasets))
