"""One-time catalog restructure transform.

Reads the current v2 catalog Data files plus v1 NPC/rank-up/faction sources and
emits the new faction-grouped layout:

  Data/units/units-<slug>.json          { alliance, factionId, name, characters[], mows[] }
  Data/npcs/npcs-<slug>.json            { alliance, factionId, name, npcs[] }   (+ npcs-objects.json)
  Data/campaign-battles/campaign-battles-<group>.json   [ battle, ... ]
  Data/mow-upgrade-costs.json           [ cost, ... ]
  Data/catalog-manifest.json            regenerated dataset list

Deletes the now-obsolete units.json / mows.json / campaign-battles.json.
"""
import json
import os
import re

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
V1 = "D:/repos/tacticus/v1/tacticusplanner/src/fsd/4-entities/character/data"
FACTIONS_JSON = "D:/repos/tacticus/v1/tacticusplanner/src/data/factions.json"


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


def slug(value):
    s = value.lower().replace("'", "")
    s = re.sub(r"[^a-z0-9]+", "-", s).strip("-")
    return s


# ---- sources ----------------------------------------------------------------
units = load(os.path.join(DATA, "units.json"))
mows_doc = load(os.path.join(DATA, "mows.json"))
mows = mows_doc["mows"]
mow_upgrade_costs = mows_doc["upgradeCosts"]
campaigns = load(os.path.join(DATA, "campaigns.json"))
campaign_events = load(os.path.join(DATA, "campaign-events.json"))
campaign_battles = load(os.path.join(DATA, "campaign-battles.json"))
npc_src = load(os.path.join(V1, "new-npc-data.json"))
rank_src = load(os.path.join(V1, "new-rank-up-data.json"))
faction_meta = {f["snowprintId"]: f for f in load(FACTIONS_JSON)}


def meta(snowprint_id, fallback_alliance=""):
    m = faction_meta.get(snowprint_id)
    if m:
        return {"alliance": m["alliance"], "name": m["name"]}
    return {"alliance": fallback_alliance, "name": snowprint_id}


# ---- characters + mows per faction -----------------------------------------
def character_record(u):
    rank_ups = [
        {"rank": rank, "upgradeIds": ids}
        for rank, ids in rank_src.get(u["id"], {}).items()
    ]
    return {
        "id": u["id"],
        "name": u["name"],
        "health": u["health"],
        "damage": u["damage"],
        "armour": u["armour"],
        "initialRarity": u["initialRarity"],
        "meleeDamage": u["meleeDamage"],
        "meleeHits": u["meleeHits"],
        "rangedDamage": u.get("rangedDamage"),
        "rangedHits": u.get("rangedHits"),
        "rangeDistance": u.get("rangeDistance"),
        "movement": u["movement"],
        "traits": u.get("traits", []),
        "activeAbilityNames": u.get("activeAbilityNames", []),
        "passiveAbilityNames": u.get("passiveAbilityNames", []),
        "equipmentSlots": u.get("equipmentSlots", []),
        "icon": u["icon"],
        "roundIcon": u["roundIcon"],
        "rankUpUpgrades": rank_ups,
    }


def mow_record(m):
    return {
        "id": m["id"],
        "name": m["name"],
        "icon": m["icon"],
        "roundIcon": m["roundIcon"],
        "primaryAbility": m["primaryAbility"],
        "secondaryAbility": m["secondaryAbility"],
    }


unit_factions = sorted({u["faction"] for u in units})
unit_keys = []
for fac in unit_factions:
    info = meta(fac, next((u["alliance"] for u in units if u["faction"] == fac), ""))
    chars = [character_record(u) for u in units if u["faction"] == fac]
    fac_mows = [mow_record(m) for m in mows if m["faction"] == fac]
    doc = {
        "alliance": info["alliance"],
        "factionId": fac,
        "name": info["name"],
        "characters": chars,
        "mows": fac_mows,
    }
    key = f"units-{slug(fac)}"
    unit_keys.append(key)
    write(os.path.join(DATA, "units", f"{key}.json"), doc)

# Guard: every mow landed in a faction file.
assert sum(len(load(os.path.join(DATA, "units", f"{k}.json"))["mows"]) for k in unit_keys) == len(mows)
assert sum(len(load(os.path.join(DATA, "units", f"{k}.json"))["characters"]) for k in unit_keys) == len(units)

write(os.path.join(DATA, "mow-upgrade-costs.json"), mow_upgrade_costs)


# ---- npcs per faction -------------------------------------------------------
def npc_record(n):
    rec = {
        "id": n["id"],
        "name": n.get("Name", ""),
        "meleeDamage": n.get("Melee Damage", ""),
        "meleeHits": n.get("Melee Hits", 0),
        "rangedDamage": n.get("Ranged Damage"),
        "rangedHits": n.get("Ranged Hits"),
        "distance": n.get("Distance"),
        "movement": n.get("Movement", 0),
        "traits": n.get("Traits", []),
        "activeAbilityDamage": n.get("Active Ability Damage", []),
        "activeAbilities": n.get("Active Abilities", []),
        "passiveAbilityDamage": n.get("Passive Ability Damage", []),
        "passiveAbilities": n.get("Passive Abilities", []),
        "icon": n.get("Icon"),
        "stats": [
            {
                "abilityLevel": s.get("AbilityLevel", 0),
                "damage": s.get("Damage", 0),
                "armour": s.get("Armor", 0),
                "health": s.get("Health", 0),
                "progressionIndex": s.get("ProgressionIndex", 0),
                "rank": s.get("Rank", 0),
                "stars": s.get("Stars", 0),
            }
            for s in n.get("Stats", [])
        ],
    }
    return rec


npc_by_faction = {}
for n in npc_src:
    npc_by_faction.setdefault(n.get("Faction", "") or "", []).append(n)

npc_keys = []
for fac in sorted(npc_by_faction):
    records = [npc_record(n) for n in npc_by_faction[fac]]
    if fac == "":
        factionId, fac_slug, name, alliance = "Objects", "objects", "Objects", "Neutral"
    else:
        info = meta(fac, npc_by_faction[fac][0].get("Alliance", ""))
        factionId, fac_slug, name, alliance = fac, slug(fac), info["name"], info["alliance"]
    doc = {
        "alliance": alliance,
        "factionId": factionId,
        "name": name,
        "npcs": records,
    }
    key = f"npcs-{fac_slug}"
    npc_keys.append(key)
    write(os.path.join(DATA, "npcs", f"{key}.json"), doc)

assert sum(len(load(os.path.join(DATA, "npcs", f"{k}.json"))["npcs"]) for k in npc_keys) == len(npc_src)
# objects bucket sorts after faction buckets for readability
npc_keys = [k for k in npc_keys if k != "npcs-objects"] + (["npcs-objects"] if "npcs-objects" in npc_keys else [])


# ---- campaign battles split -------------------------------------------------
campaign_by_id = {c["id"]: c for c in campaigns + campaign_events}


def battle_group_key(battle):
    c = campaign_by_id[battle["campaign"]]
    g = slug(c["groupType"])
    if c["releaseType"] == "standard" and "Mirror" in battle["campaign"]:
        g += "-mirror"
    return f"campaign-battles-{g}"


battles_by_group = {}
for b in campaign_battles:
    battles_by_group.setdefault(battle_group_key(b), []).append(b)

# Deterministic order: standard storylines first (as in campaigns.json), then events.
ordered_groups = []
seen = set()
for c in campaigns + campaign_events:
    g = slug(c["groupType"])
    for variant in (g, g + "-mirror"):
        key = f"campaign-battles-{variant}"
        if key in battles_by_group and key not in seen:
            seen.add(key)
            ordered_groups.append(key)

assert set(ordered_groups) == set(battles_by_group), (set(battles_by_group) - set(ordered_groups))
assert sum(len(v) for v in battles_by_group.values()) == len(campaign_battles)
battle_keys = ordered_groups
for key in battle_keys:
    write(os.path.join(DATA, "campaign-battles", f"{key}.json"), battles_by_group[key])


# ---- manifest ---------------------------------------------------------------
def entry(key, path):
    return {"key": key, "file": path}


datasets = []
for k in unit_keys:
    datasets.append(entry(k, f"units/{k}.json"))
datasets.append(entry("mow-upgrade-costs", "mow-upgrade-costs.json"))
for k in npc_keys:
    datasets.append(entry(k, f"npcs/{k}.json"))
datasets.append(entry("upgrades", "upgrades.json"))
datasets.append(entry("equipment", "equipment.json"))
datasets.append(entry("campaigns", "campaigns.json"))
datasets.append(entry("campaign-events", "campaign-events.json"))
for k in battle_keys:
    datasets.append(entry(k, f"campaign-battles/{k}.json"))
datasets.append(entry("lres", "lres.json"))

manifest = {"version": "dev-2026-06-24", "schemaVersion": 2, "datasets": datasets}
write(os.path.join(DATA, "catalog-manifest.json"), manifest)


# ---- cleanup ----------------------------------------------------------------
for old in ("units.json", "mows.json", "campaign-battles.json"):
    p = os.path.join(DATA, old)
    if os.path.exists(p):
        os.remove(p)


# ---- emit C# registry snippets ---------------------------------------------
print("UNIT_KEYS =", unit_keys)
print("NPC_KEYS =", npc_keys)
print("BATTLE_KEYS =", battle_keys)
print("counts: units_factions=%d npc_files=%d battle_groups=%d total_datasets=%d"
      % (len(unit_keys), len(npc_keys), len(battle_keys), len(datasets)))
