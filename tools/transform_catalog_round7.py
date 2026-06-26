"""Round-7 catalog transform (runs on the post-round-6 Data tree).

Two things:

1. Normalize the inconsistent `rank` values to one canonical, space-free, id-like form
   `<Tier><arabic>` (e.g. `Stone1`, `Iron2`, `Diamond3`):
     - units/units-*.json            -> characters[].rankUpUpgrades[].rank   ("Stone I" -> "Stone1")
     - campaign-battles/*.json       -> battles[].detailedEnemyTypes[].rank  ("Iron 2"  -> "Iron2")

2. Emit a plain reference file `Data/enums.json` cataloguing the canonical (space-free) form plus a
   human display label for all five enumerated dimensions used across the catalog: ranks, rarities,
   factions, traits, alliances. This file is a reference only -- it is intentionally NOT added to the
   manifest and is not served or validated.

`rank` is a free-form display string referenced by no other dataset and not stored in the manifest
(keys/files only; hashes computed at runtime), so no schemaVersion bump is needed.
"""
import json
import os
import glob
import re

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
UNITS_DIR = os.path.join(DATA, "units")
NPCS_DIR = os.path.join(DATA, "npcs")
CB_DIR = os.path.join(DATA, "campaign-battles")


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


# ---- rank normalization ----------------------------------------------------------------------
ROMAN_TO_ARABIC = {"I": 1, "II": 2, "III": 3}
ARABIC_TO_ROMAN = {1: "I", 2: "II", 3: "III"}
TIER_ORDER = ["Stone", "Iron", "Bronze", "Silver", "Gold", "Diamond", "Adamantine"]
# accepts the original spaced forms ("Stone I", "Iron 2") and the normalized form ("Stone1")
_RANK_RE = re.compile(r"^([A-Za-z]+) ?(I{1,3}|[1-9])$")

# distinct (tier, level) pairs seen, to build the canonical ranks list from real data
rank_levels = set()


def parse_rank(value):
    m = _RANK_RE.match(value)
    if not m:
        raise ValueError(f"unrecognized rank value: {value!r}")
    tier, numeral = m.group(1), m.group(2)
    level = ROMAN_TO_ARABIC[numeral] if numeral in ROMAN_TO_ARABIC else int(numeral)
    if tier not in TIER_ORDER:
        raise ValueError(f"unknown rank tier: {tier!r}")
    rank_levels.add((tier, level))
    return f"{tier}{level}"


units_renamed = 0
for path in sorted(glob.glob(os.path.join(UNITS_DIR, "*.json"))):
    f = load(path)
    changed = False
    for c in f["characters"]:
        for ru in c.get("rankUpUpgrades", []):
            new = parse_rank(ru["rank"])
            if new != ru["rank"]:
                ru["rank"] = new
                units_renamed += 1
                changed = True
    if changed:
        write(path, f)

battles_renamed = 0
for path in sorted(glob.glob(os.path.join(CB_DIR, "campaign-battles-*.json"))):
    g = load(path)
    changed = False
    for b in g["battles"]:
        for e in b.get("detailedEnemyTypes", []):
            if "rank" not in e or e["rank"] is None:
                continue
            new = parse_rank(e["rank"])
            if new != e["rank"]:
                e["rank"] = new
                battles_renamed += 1
                changed = True
    if changed:
        write(path, g)

# fail loudly if any rank value still contains a space
for path in glob.glob(os.path.join(UNITS_DIR, "*.json")):
    for c in load(path)["characters"]:
        for ru in c.get("rankUpUpgrades", []):
            assert " " not in ru["rank"], ru["rank"]
for path in glob.glob(os.path.join(CB_DIR, "campaign-battles-*.json")):
    for b in load(path)["battles"]:
        for e in b.get("detailedEnemyTypes", []):
            if e.get("rank"):
                assert " " not in e["rank"], e["rank"]


# ---- enums.json reference --------------------------------------------------------------------
def split_camel(s):
    # "ActOfFaith" -> "Act Of Faith", "MkXGravis" -> "Mk X Gravis"
    s = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", " ", s)
    s = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", s)
    return s


def distinct_sorted(values):
    return sorted(set(values))


ranks = [
    {"id": f"{tier}{level}", "displayName": f"{tier} {ARABIC_TO_ROMAN[level]}"}
    for tier in TIER_ORDER
    for level in sorted({lvl for (t, lvl) in rank_levels if t == tier})
]

rarity_values = set()
for path in glob.glob(os.path.join(UNITS_DIR, "*.json")):
    for c in load(path)["characters"]:
        if c.get("initialRarity"):
            rarity_values.add(c["initialRarity"])
for path in glob.glob(os.path.join(DATA, "upgrades", "*.json")):
    for u in load(path):
        if isinstance(u, dict) and u.get("rarity"):
            rarity_values.add(u["rarity"])
rarities = [{"id": r, "displayName": r} for r in distinct_sorted(rarity_values)]

# faction display name = the faction object's `name` (from units datasets)
faction_name = {}
for path in glob.glob(os.path.join(UNITS_DIR, "*.json")):
    f = load(path)
    faction_name[f["factionId"]] = f["name"]
factions = [
    {"id": fid, "displayName": faction_name[fid]} for fid in distinct_sorted(faction_name)
]

trait_values = set()
alliance_values = set()
for d in (UNITS_DIR, NPCS_DIR):
    for path in glob.glob(os.path.join(d, "*.json")):
        f = load(path)
        if f.get("alliance"):
            alliance_values.add(f["alliance"])
        members = f.get("characters", []) + f.get("npcs", [])
        for m in members:
            for t in m.get("traits", []) or []:
                trait_values.add(t)
traits = [{"id": t, "displayName": split_camel(t)} for t in distinct_sorted(trait_values)]
alliances = [{"id": a, "displayName": a} for a in distinct_sorted(alliance_values)]

write(os.path.join(DATA, "enums.json"), {
    "ranks": ranks,
    "rarities": rarities,
    "factions": factions,
    "traits": traits,
    "alliances": alliances,
})

print(
    f"ranks normalized: units={units_renamed} battles={battles_renamed} | "
    f"enums -> ranks={len(ranks)} rarities={len(rarities)} factions={len(factions)} "
    f"traits={len(traits)} alliances={len(alliances)}"
)
