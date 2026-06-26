"""Round-9 catalog transform (runs on the post-round-8 Data tree).

Extract the equipment per-level cost ladder -- which is fully determined by rarity -- out of the 212
items in `Data/equipment/equipment-*.json` and into one shared `Data/equipment-upgrade-costs.json`
(mirroring `mow-upgrade-costs.json`). Across all items there is exactly ONE distinct
`(goldCost, salvageCost, mythicSalvageCost)` sequence per rarity, and the level count is fixed per
rarity, so the three cost ints were duplicated across 1,636 level entries.

After this transform each item `levels[i]` is just `{ "stats": {...} }`; a consumer rebuilds the cost
by looking up the item's `rarity` in the shared table and aligning by level index (item level i <->
ladder index i). `CatalogEquipment.Levels` is opaque (IReadOnlyList<JsonElement>), so dropping the
cost fields needs no C# model change; the new cost table gets its own typed record + endpoint.

The canonical ladders below are transcribed from the verified source data. The transform is
self-validating and idempotent: for any level that still carries cost fields it asserts they match the
canonical ladder before stripping; on a re-run (levels already stripped) it re-emits the same table
from the literals. schemaVersion is bumped 7 -> 8 (equipment served shape shrinks + a new dataset).
"""
import json
import os
import glob

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
EQUIP_DIR = os.path.join(DATA, "equipment")

# rarity -> ladder of (goldCost, salvageCost, mythicSalvageCost), one tuple per upgrade level.
LADDERS = {
    "Common": [(0, 10, 0), (50, 5, 0), (100, 10, 0)],
    "Uncommon": [(0, 25, 0), (100, 10, 0), (150, 15, 0), (200, 20, 0), (250, 30, 0)],
    "Rare": [(0, 60, 0), (250, 30, 0), (350, 40, 0), (450, 60, 0), (550, 90, 0), (650, 120, 0),
             (750, 150, 0)],
    "Epic": [(0, 150, 0), (750, 90, 0), (1000, 120, 0), (1250, 150, 0), (1500, 200, 0),
             (1750, 250, 0), (2000, 300, 0), (2250, 350, 0), (2500, 400, 0)],
    "Legendary": [(0, 400, 0), (2000, 250, 0), (3000, 350, 0), (4000, 450, 0), (6000, 600, 0),
                  (8000, 800, 0), (10000, 1000, 0), (15000, 1250, 0), (20000, 1500, 0),
                  (30000, 2000, 0), (50000, 3000, 0)],
    "Mythic": [(0, 0, 100), (50000, 0, 50), (60000, 0, 75), (70000, 0, 100), (85000, 0, 125),
               (100000, 0, 150), (125000, 0, 200), (150000, 0, 300), (175000, 0, 500),
               (200000, 0, 1000)],
}
# emit order for the shared table
RARITY_ORDER = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"]
COST_KEYS = ("goldCost", "salvageCost", "mythicSalvageCost")


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


# ---- strip cost fields from every item level -------------------------------------------------
items_total = 0
levels_stripped = 0
for path in sorted(glob.glob(os.path.join(EQUIP_DIR, "equipment-*.json"))):
    items = load(path)
    for item in items:
        items_total += 1
        rarity = item["rarity"]
        ladder = LADDERS[rarity]
        levels = item["levels"]
        assert len(levels) == len(ladder), (path, item["id"], len(levels), len(ladder))
        new_levels = []
        for i, lvl in enumerate(levels):
            extra = set(lvl) - set(COST_KEYS) - {"stats"}
            assert not extra, (path, item["id"], i, extra)
            if all(k in lvl for k in COST_KEYS):
                got = tuple(lvl[k] for k in COST_KEYS)
                assert got == ladder[i], (path, item["id"], rarity, i, got, ladder[i])
                levels_stripped += 1
            new_levels.append({"stats": lvl["stats"]})
        item["levels"] = new_levels
    write(path, items)

# ---- emit the shared cost table --------------------------------------------------------------
cost_table = [
    {
        "rarity": rarity,
        "levels": [
            {"goldCost": g, "salvageCost": s, "mythicSalvageCost": m}
            for (g, s, m) in LADDERS[rarity]
        ],
    }
    for rarity in RARITY_ORDER
]
write(os.path.join(DATA, "equipment-upgrade-costs.json"), cost_table)


# ---- regenerate manifest (schemaVersion 8) ---------------------------------------------------
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
      {"version": "dev-2026-06-25", "schemaVersion": 8, "datasets": datasets})

print(
    f"equipment costs extracted: items={items_total} levels_stripped={levels_stripped} "
    f"rarities={len(cost_table)} | datasets={len(datasets)} schemaVersion=8"
)
