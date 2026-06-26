"""Round-2 catalog transform (runs on the post-round-1 Data tree).

- Merge campaigns.json + campaign-events.json into each campaign-battles-<group>.json,
  turning those files from plain battle arrays into chunk objects:
      { groupId, groupType, releaseType, campaigns: [...], battles: [...] }
- Split equipment.json by type, upgrades.json by rarity into subfolders.
- Split lres.json by event keyed on the unit snowprintId, keeping only sourceFiles 10/11/12.
- Regenerate catalog-manifest.json (schemaVersion 3) by scanning the resulting tree.
- Delete the now-obsolete top-level files.
"""
import json
import os
import re
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


def slug(value):
    return re.sub(r"[^a-z0-9]+", "-", value.lower().replace("'", "")).strip("-")


# ---- 1. campaigns -> battle groups -----------------------------------------
campaigns = load(os.path.join(DATA, "campaigns.json")) + load(os.path.join(DATA, "campaign-events.json"))


def group_key(campaign):
    g = slug(campaign["groupType"])
    if campaign["releaseType"] == "standard" and "Mirror" in campaign["id"]:
        g += "-mirror"
    return g


campaigns_by_group = {}
for c in campaigns:
    campaigns_by_group.setdefault(group_key(c), []).append(c)

campaign_total = 0
for path in glob.glob(os.path.join(DATA, "campaign-battles", "campaign-battles-*.json")):
    battles = load(path)
    group_id = os.path.basename(path)[len("campaign-battles-"):-len(".json")]
    group_campaigns = campaigns_by_group[group_id]
    campaign_total += len(group_campaigns)
    write(path, {
        "groupId": group_id,
        "groupType": group_campaigns[0]["groupType"],
        "releaseType": group_campaigns[0]["releaseType"],
        "campaigns": group_campaigns,
        "battles": battles,
    })

assert campaign_total == len(campaigns), (campaign_total, len(campaigns))


# ---- 2. equipment by type ---------------------------------------------------
def equipment_type_slug(t):
    return (t[2:] if t.startswith("I_") else t).lower().replace("_", "-")


equipment = load(os.path.join(DATA, "equipment.json"))
by_type = {}
for e in equipment:
    by_type.setdefault(equipment_type_slug(e["type"]), []).append(e)
for key, items in by_type.items():
    write(os.path.join(DATA, "equipment", f"equipment-{key}.json"), items)
assert sum(len(v) for v in by_type.values()) == len(equipment)


# ---- 3. upgrades by rarity --------------------------------------------------
upgrades = load(os.path.join(DATA, "upgrades.json"))
by_rarity = {}
for u in upgrades:
    by_rarity.setdefault(u["rarity"].lower(), []).append(u)
for key, items in by_rarity.items():
    write(os.path.join(DATA, "upgrades", f"upgrades-{key}.json"), items)
assert sum(len(v) for v in by_rarity.values()) == len(upgrades)


# ---- 4. lres by event (keep sourceFiles 10/11/12) ---------------------------
lres = load(os.path.join(DATA, "lres.json"))


def source_number(record):
    m = re.match(r"(\d+)", record.get("sourceFile", ""))
    return int(m.group(1)) if m else -1


kept = [r for r in lres if source_number(r) in (10, 11, 12)]
assert len(kept) == 3, [r.get("sourceFile") for r in kept]
for r in kept:
    key = r["unitSnowprintId"].lower()
    write(os.path.join(DATA, "lres", f"lres-{key}.json"), r)


# ---- 5. cleanup -------------------------------------------------------------
for old in ("campaigns.json", "campaign-events.json", "equipment.json", "upgrades.json", "lres.json"):
    p = os.path.join(DATA, old)
    if os.path.exists(p):
        os.remove(p)


# ---- 6. regenerate manifest by scanning the tree ----------------------------
def datasets_in(subdir):
    out = []
    for path in sorted(glob.glob(os.path.join(DATA, subdir, "*.json"))):
        name = os.path.basename(path)
        out.append({"key": name[:-len(".json")], "file": f"{subdir}/{name}"})
    return out


datasets = []
datasets += datasets_in("units")
datasets.append({"key": "mow-upgrade-costs", "file": "mow-upgrade-costs.json"})
datasets += datasets_in("npcs")
datasets += datasets_in("equipment")
datasets += datasets_in("upgrades")
datasets += datasets_in("campaign-battles")
datasets += datasets_in("lres")

manifest = {"version": "dev-2026-06-24", "schemaVersion": 3, "datasets": datasets}
write(os.path.join(DATA, "catalog-manifest.json"), manifest)


# ---- emit registry snippets -------------------------------------------------
print("EQUIPMENT_KEYS =", sorted(f"equipment-{k}" for k in by_type))
print("UPGRADE_KEYS =", sorted(f"upgrades-{k}" for k in by_rarity))
print("LRE_KEYS =", sorted(f"lres-{r['unitSnowprintId'].lower()}" for r in kept))
print("counts: campaigns=%d equipment=%d upgrades=%d lres=%d total_datasets=%d"
      % (len(campaigns), len(equipment), len(upgrades), len(kept), len(datasets)))
