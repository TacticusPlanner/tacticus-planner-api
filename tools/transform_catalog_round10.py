"""Round-10 catalog transform (runs on the post-round-9 Data tree).

Two things:

1. Decompose the raw upgrades source by rarity x crafted. Each rarity's NON-craftable items stay in
   `upgrades/upgrades-{rarity}.json`; its craftable items move to `upgrades/upgrades-{rarity}-crafted.json`,
   which is written only when that rarity actually has craftable items (Common has none). This is the only
   raw-files-layout change; upgrades are now SERVED consolidated/denormalized by the API, so these files
   feed loading, not the served manifest.

2. Regenerate the (internal source) `catalog-manifest.json`: list every raw file, add `gameVersion`
   ("1.40"), and bump schemaVersion 8 -> 9 (the served catalog shape changes with denormalization).

Idempotent: re-running gathers items from all `upgrades-{rarity}*.json`, recombines, then re-partitions,
so the base/crafted split is stable. Self-validating: asserts each craftable item's recipe is non-empty
and every recipe `material` id resolves across the full upgrade set.
"""
import json
import os
import glob

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
UPG_DIR = os.path.join(DATA, "upgrades")

GAME_VERSION = "1.40"
SCHEMA_VERSION = 9
RARITIES = ["common", "uncommon", "rare", "epic", "legendary", "mythic"]


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


# ---- decompose upgrades by rarity x crafted --------------------------------------------------
all_ids = set()
partition = {}  # rarity -> (base[], crafted[])
for rarity in RARITIES:
    items = []
    for path in sorted(glob.glob(os.path.join(UPG_DIR, f"upgrades-{rarity}*.json"))):
        items += load(path)
    base = [u for u in items if not u.get("craftable")]
    crafted = [u for u in items if u.get("craftable")]
    partition[rarity] = (base, crafted)
    all_ids.update(u["id"] for u in items)

# validate craftable recipes resolve across the whole set
for rarity, (base, crafted) in partition.items():
    for u in crafted:
        assert u.get("recipe"), ("empty recipe", u["id"])
        for ing in u["recipe"]:
            assert ing["material"] in all_ids, ("unresolved ingredient", u["id"], ing["material"])

# write base always; write crafted only when non-empty; remove stale empty crafted files
base_files = crafted_files = 0
for rarity, (base, crafted) in partition.items():
    write(os.path.join(UPG_DIR, f"upgrades-{rarity}.json"), base)
    base_files += 1
    crafted_path = os.path.join(UPG_DIR, f"upgrades-{rarity}-crafted.json")
    if crafted:
        write(crafted_path, crafted)
        crafted_files += 1
    elif os.path.exists(crafted_path):
        os.remove(crafted_path)


# ---- regenerate internal source manifest (schemaVersion 9, gameVersion) ----------------------
def datasets_in(subdir):
    return [
        {"key": os.path.basename(p)[: -len(".json")], "file": f"{subdir}/{os.path.basename(p)}"}
        for p in sorted(glob.glob(os.path.join(DATA, subdir, "*.json")))
    ]


datasets = []
datasets += datasets_in("units")
datasets.append({"key": "mow-upgrade-costs", "file": "mow-upgrade-costs.json"})
datasets.append({"key": "equipment-upgrade-costs", "file": "equipment-upgrade-costs.json"})
datasets.append({"key": "drop-chances", "file": "drop-chances.json"})
datasets += datasets_in("npcs")
datasets += datasets_in("equipment")
datasets += datasets_in("upgrades")
datasets += datasets_in("campaign-battles")
datasets += datasets_in("lres")

write(os.path.join(DATA, "catalog-manifest.json"),
      {"version": "dev-2026-06-25", "schemaVersion": SCHEMA_VERSION,
       "gameVersion": GAME_VERSION, "datasets": datasets})

print(
    f"upgrades decomposed: base_files={base_files} crafted_files={crafted_files} "
    f"total_upgrades={len(all_ids)} | source datasets={len(datasets)} "
    f"schemaVersion={SCHEMA_VERSION} gameVersion={GAME_VERSION}"
)
for rarity in RARITIES:
    base, crafted = partition[rarity]
    print(f"  {rarity:10} base={len(base):4} crafted={len(crafted):4}")
