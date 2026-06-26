"""Round-8 catalog transform (runs on the post-round-7 Data tree).

Move the per-track LRE "common data" that lived in v1 hand-written TS classes
(`tacticus-planner/src/fsd/3-features/lre/model/*.le.ts`) into the v2 catalog JSON so the
frontend can reproduce each track from the catalog instead of hardcoded classes. Per track we add:

  - `allowedUnitsFilter`: the track-level allowed-units restriction (alliance/faction pre-filter,
    AND-combined, all exclusions) -- captures the compound cases (Lucius beta = no Chaos + no Tyranids,
    Farsight gamma = no Chaos + no Orks) that the track display name silently drops.
  - `unitsRestrictions`: the 5 objectives, each `{ name, points, iconId, index, filter }` where `filter`
    is the structured `{ kind, target, exclude }` form (v1's `Not*` objectiveType prefix becomes
    `exclude: true`). The v1 `units` array is computed at runtime from the filter, so it is not stored.

Only the 3 LREs v2 keeps are populated. schemaVersion is bumped 6 -> 7 (the served lres shape grows).
The data below is transcribed from the three v1 `*.le.ts` files (10-lucius, 11-farsight, 12-uthar).
Idempotent: re-running overwrites the same track fields with identical values.
"""
import json
import os
import glob

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
LRES_DIR = os.path.join(DATA, "lres")


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


# v1 objectiveType -> (kind, exclude). target is supplied per-objective.
OBJECTIVE_KIND = {
    "Faction": ("Faction", False),
    "Trait": ("Trait", False),
    "NotTrait": ("Trait", True),
    "DamageType": ("DamageType", False),
    "NotDamageType": ("DamageType", True),
    "MinHits": ("MinHits", False),
    "MaxHits": ("MaxHits", False),
    "HasRangedAttack": ("AttackType", False),
    "HasNoRangedAttack": ("AttackType", True),
}

# objectiveType values whose canonical target is "Ranged" regardless of the v1 (empty) objectiveTarget.
ATTACK_TYPE = {"HasRangedAttack", "HasNoRangedAttack"}


def objective(name, points, icon_id, objective_type, target):
    kind, exclude = OBJECTIVE_KIND[objective_type]
    if objective_type in ATTACK_TYPE:
        target = "Ranged"
    return {"name": name, "points": points, "iconId": icon_id, "filter": filt(kind, target, exclude)}


def filt(kind, target, exclude):
    return {"kind": kind, "target": str(target), "exclude": exclude}


def exclude_all(*pairs):
    """Track-level restriction: a list of `{kind, target, exclude:true}` filters (AND-combined)."""
    return [filt(kind, target, True) for (kind, target) in pairs]


# event filename (without .json) -> { track -> (allowedUnitsFilter, [objectives...]) }
EVENTS = {
    "lres-emperlucius": {
        "alpha": (
            exclude_all(("Alliance", "Xenos")),
            [
                objective("Black Legion", 85, "black_legion", "Faction", "BlackLegion"),
                objective("Bolter", 95, "bolter", "DamageType", "Bolter"),
                objective("Min 4 Hits", 45, "hits", "MinHits", "4"),
                objective("Flying", 90, "flying", "Trait", "Flying"),
                objective("No Bolter", 60, "no_bolter", "NotDamageType", "Bolter"),
            ],
        ),
        "beta": (
            exclude_all(("Alliance", "Chaos"), ("Faction", "Tyranids")),
            [
                objective("Heavy Weapon", 95, "heavy_weapon", "Trait", "HeavyWeapon"),
                objective("Piercing", 65, "piercing", "DamageType", "Piercing"),
                objective("Unstoppable", 95, "unstoppable", "Trait", "Unstoppable"),
                objective("Physical", 45, "physical", "DamageType", "Physical"),
                objective("Max 2 hits", 75, "hits", "MaxHits", "2"),
            ],
        ),
        "gamma": (
            exclude_all(("Alliance", "Imperial")),
            [
                objective("Ranged Specialist", 105, "ranged_specialist", "Trait", "RangedSpecialist"),
                objective("Psychic", 80, "psychic", "DamageType", "Psychic"),
                objective("No Power", 35, "no_power", "NotDamageType", "Power"),
                objective("Min 5 hits", 95, "hits", "MinHits", "5"),
                objective("Physical", 60, "physical", "DamageType", "Physical"),
            ],
        ),
    },
    "lres-taufarsight": {
        "alpha": (
            exclude_all(("Alliance", "Imperial")),
            [
                objective("No Terminator", 65, "no_terminator", "NotTrait", "TerminatorArmour"),
                objective("Power", 70, "power", "DamageType", "Power"),
                objective("Min 5 Hits", 45, "hits", "MinHits", "5"),
                objective("Flame", 90, "flame", "DamageType", "Flame"),
                objective("Eviscerate", 105, "eviscerate", "DamageType", "Eviscerate"),
            ],
        ),
        "beta": (
            exclude_all(("Alliance", "Xenos")),
            [
                objective("Astra Militarum", 75, "astra_militarum", "Faction", "AstraMilitarum"),
                objective("Bolter", 65, "bolter", "DamageType", "Bolter"),
                objective("Resilient", 75, "resilient", "Trait", "Resilient"),
                objective("Piercing", 105, "piercing", "DamageType", "Piercing"),
                objective("Max 2 hits", 55, "hits", "MaxHits", "2"),
            ],
        ),
        "gamma": (
            exclude_all(("Alliance", "Chaos"), ("Faction", "Orks")),
            [
                objective("No Physical", 45, "no_physical", "NotDamageType", "Physical"),
                objective("Blast", 90, "blast", "DamageType", "Blast"),
                objective("Ranged", 45, "ranged", "HasRangedAttack", ""),
                objective("Max 1 hit", 90, "hits", "MaxHits", "1"),
                objective("Flying", 105, "flying", "Trait", "Flying"),
            ],
        ),
    },
    "lres-votanuthar": {
        "alpha": (
            exclude_all(("Alliance", "Xenos")),
            [
                objective("Ultramarines", 75, "ultramarines", "Faction", "Ultramarines"),
                objective("Resilient", 95, "resilient", "Trait", "Resilient"),
                objective("Max 2 Hits", 50, "hits", "MaxHits", "2"),
                objective("Melee", 65, "melee", "HasNoRangedAttack", ""),
                objective("Piercing", 90, "piercing", "DamageType", "Piercing"),
            ],
        ),
        "beta": (
            exclude_all(("Alliance", "Imperial")),
            [
                objective("Ranged", 60, "ranged", "HasRangedAttack", ""),
                objective("No Psyker", 55, "no_psyker", "NotTrait", "Psyker"),
                objective("Terminator", 85, "terminator", "Trait", "TerminatorArmour"),
                objective("Psychic Damage", 100, "psychic", "DamageType", "Psychic"),
                objective("Min 5 Hits", 75, "hits", "MinHits", "5"),
            ],
        ),
        "gamma": (
            exclude_all(("Alliance", "Chaos")),
            [
                objective("No Power", 50, "no_power", "NotDamageType", "Power"),
                objective("Bolter", 85, "bolter", "DamageType", "Bolter"),
                objective("Mechanical", 100, "mechanical", "Trait", "Mechanical"),
                objective("Max 1 Hit", 75, "hits", "MaxHits", "1"),
                objective("Big Target", 65, "big_target", "Trait", "BigTarget"),
            ],
        ),
    },
}


# ---- inject per-track requirements/restrictions ----------------------------------------------
total_objectives = 0
for event_key, tracks in EVENTS.items():
    path = os.path.join(LRES_DIR, f"{event_key}.json")
    lre = load(path)
    for track_id, (allowed_filter, objectives) in tracks.items():
        track = lre[track_id]
        track["allowedUnitsFilter"] = allowed_filter
        track["unitsRestrictions"] = [
            {**o, "index": i} for i, o in enumerate(objectives)
        ]
        total_objectives += len(objectives)
    write(path, lre)

# fail loudly if any populated track is malformed
for event_key, tracks in EVENTS.items():
    lre = load(os.path.join(LRES_DIR, f"{event_key}.json"))
    for track_id in tracks:
        track = lre[track_id]
        assert len(track["unitsRestrictions"]) == 5, (event_key, track_id)
        assert track["allowedUnitsFilter"], (event_key, track_id)
        for i, r in enumerate(track["unitsRestrictions"]):
            assert r["index"] == i, (event_key, track_id, i)
            assert set(r["filter"]) == {"kind", "target", "exclude"}, r["filter"]


# ---- regenerate manifest (schemaVersion 7) ---------------------------------------------------
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
      {"version": "dev-2026-06-25", "schemaVersion": 7, "datasets": datasets})

print(
    f"lres populated: events={len(EVENTS)} tracks={len(EVENTS) * 3} objectives={total_objectives} "
    f"| datasets={len(datasets)} schemaVersion=7"
)
