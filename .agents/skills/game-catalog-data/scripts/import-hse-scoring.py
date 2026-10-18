"""Builds Data/events/home-screen-event-scoring.json from V1's home-screen-event datamine.

Usage (from the tacticus-planner-api root, with the V1 repo checked out as a sibling):

    python3 .agents/skills/game-catalog-data/scripts/import-hse-scoring.py ../tacticusplanner

Each V1 file maps to one (definition id, variant key) pair in SOURCES. See the
add-hse-points-projection change (design D3, D4) for the normalization rules.
"""

import json
import pathlib
import sys

# V1 file stem -> (definition id, variant key, scope)
SOURCES = {
    "trait-boost-psyker": ("hse-trait-boost-psyker", "default", "Personal"),
    "trait-boost-flying": ("hse-trait-boost-flying", "default", "Personal"),
    "hse-trait-boost-rapid-assault": ("hse-trait-boost-rapid-assault", "default", "Personal"),
    "hse-trait-boost-terminator-armour": ("hse-terminator-boost", "default", "Personal"),
    "for-the-emperor": ("hse-for-the-emperor", "default", "Personal"),
    "for-the-dark-gods": ("hse-for-the-dark-gods", "default", "Personal"),
    "faction-boost-dark-angels": ("hse-faction-boost", "DarkAngels", "Personal"),
    "faction-boost-space-wolves": ("hse-faction-boost", "SpaceWolves", "Personal"),
    "faction-boost-adeptus-mechanicus": ("hse-faction-boost", "AdeptusMechanicus", "Personal"),
    "faction-focus": ("hse-faction-focus", "default", "Personal"),
    "hse-faction-focus-october-2026": ("hse-faction-focus", "october-2026", "Personal"),
    "defeat-waves": ("hse-defeat-waves", "default", "Personal"),
    "defeat-waves-2": ("hse-defeat-waves", "run-2", "Personal"),
    "hse-11th-edition-week1": ("hse-11th-edition-week-1", "default", "Personal"),
    "global-hse-11th-edition-week1-blood-angels": ("hse-11th-edition-week-1", "global-blood-angels", "Global"),
    "global-hse-11th-edition-week1-orks": ("hse-11th-edition-week-1", "global-orks", "Global"),
    "hse-11th-edition-week2": ("hse-11th-edition-week-2", "default", "Personal"),
    "global-hse-11th-edition-week2-blood-angels": ("hse-11th-edition-week-2", "global-blood-angels", "Global"),
    "global-hse-11th-edition-week2-orks": ("hse-11th-edition-week-2", "global-orks", "Global"),
    "hse-11th-edition-week3": ("hse-11th-edition-week-3", "default", "Personal"),
    "global-hse-11th-edition-week3-blood-angels": ("hse-11th-edition-week-3", "global-blood-angels", "Global"),
    "global-hse-11th-edition-week3-orks": ("hse-11th-edition-week-3", "global-orks", "Global"),
    "global-hse-operation-imperator": ("hse-global-operation-imperator", "default", "Global"),
    "conflict-operations": ("hse-global-conflict-operations", "default", "Global"),
    "arsenal-of-war": ("hse-arsenal-of-war", "default", "Personal"),
    "squig-smash": ("hse-squig-smash", "default", "Personal"),
    "machine-hunt": ("hse-machine-hunt", "default", "Personal"),
    "warp-surge": ("hse-warp-surge", "default", "Personal"),
    "rarity-training-rush": ("hse-training-rush", "default", "Personal"),
    "kill-tyranids": ("hse-purge-order", "default", "Personal"),
}

TIER_ORDER = ["high", "mid", "low", "default"]

GAME_MODES = {
    "PvP": "Arena",
    "SyncPVP": "TournamentArena",
    "Waves": "Onslaught",
    "TreasureBeach": "SalvageRun",
}

TRACKER_KINDS = {
    "killUnits": "KillUnits",
    "defeatWaves": "DefeatWaves",
    "deployedUnitsOfFactionMajority": "DeployedUnitsOfFactionMajority",
    "fullLineUpOfFaction": "FullLineUpOfFaction",
    "donateUpgradeToGuildMember": "DonateUpgradeToGuildMember",
    "donateIntelToGuildProject": "DonateIntelToGuildProject",
    "dispatchExpedition": "DispatchExpedition",
}


def reward(chest_reward_id):
    reward_id, _, quantity = chest_reward_id.partition(":")
    return reward_id, int(quantity) if quantity else 1


def restriction(raw, map_value=lambda value: value):
    if not raw:
        return None
    allowed = raw.get("allowed")
    disallowed = raw.get("disallowed")
    if allowed is not None and len(allowed) == 0:
        # V1's matchesRestriction treats an empty allow list as "nothing passes"; null would mean "all pass".
        raise ValueError(f"empty allowed list in restriction {raw}")
    if not allowed and not disallowed:
        return None
    return {
        "allowed": [map_value(value) for value in allowed] if allowed else None,
        "disallowed": [map_value(value) for value in disallowed] if disallowed else None,
    }


def unit_filter(tags, factions, unit_ids):
    result = {"tags": restriction(tags), "factionIds": restriction(factions), "unitIds": unit_ids}
    return result if any(value is not None for value in result.values()) else None


def game_modes(raw):
    return restriction(raw, lambda mode: GAME_MODES.get(mode, mode))


def tier(raw):
    rewards = raw["tieredProgressRewards"]
    milestones = []
    repeat = None
    for entry in rewards:
        reward_id, quantity = reward(entry["chestRewardId"])
        if entry.get("endless"):
            repeat = {
                "points": entry["requiredProgress"],
                "rewardId": reward_id,
                "quantity": quantity,
                "cap": entry.get("endlessCap"),
            }
        else:
            milestones.append({"requiredPoints": entry["requiredProgress"], "rewardId": reward_id, "quantity": quantity})
    return milestones, repeat


def trackers(raw):
    result = []
    for tracker in raw.get("liveEventConfig", {}).get("trackers", []):
        if tracker["type"] == "raidBattles":
            continue  # campaign sweep; each one's modes are covered by a killUnits tracker with equal points
        unit_id = tracker.get("unitId")
        result.append({
            "kind": TRACKER_KINDS[tracker["type"]],
            "points": tracker["points"],
            "pointsByRarity": tracker.get("pointsByRarity"),
            "gameModes": game_modes(tracker.get("gameModeRestrictions")),
            "enemy": unit_filter(tracker.get("traitRestrictions"), tracker.get("factionRestrictions"), [unit_id] if unit_id else None),
            "player": unit_filter(tracker.get("playerTraitRestrictions"), tracker.get("playerFactionRestrictions"), None),
        })
    return result


def modifiers(raw):
    result = []
    abilities = raw.get("abilities", {})
    for modifier in raw.get("liveEventConfig", {}).get("modifiers", []):
        modes = game_modes(modifier.get("gameModeRestrictions"))
        if modifier["type"] == "unitXp":
            if modifier.get("operator", "multiply") != "multiply":
                raise ValueError(f"unhandled unitXp operator {modifier['operator']}")
            scale = 100 if modifier.get("percentageModifier") else 1
            by_rarity = modifier.get("modifiers")
            result.append({
                "kind": "UnitXp",
                "multiplier": modifier["modifier"] / scale if "modifier" in modifier else None,
                "multiplierByRarity": {rarity: value / scale for rarity, value in by_rarity.items()} if by_rarity else None,
                "sameFactionTeam": bool(modifier.get("sameFactionBonus")),
                "gameModes": modes,
                "player": unit_filter(modifier.get("traitRestrictions"), modifier.get("playerFactionRestrictions"), None),
            })
        elif modifier["type"] == "battleEnvironmentAbility" and modifier["abilityId"].startswith("EnvStatBoost:"):
            constants = abilities[modifier["abilityId"]]["ability"]["constants"]
            trait = constants.get("trait")
            faction = constants.get("factionId")
            result.append({
                "kind": "StatBoost",
                "healthPct": int(constants["healthPct"]),
                "damagePct": int(constants["dmgPct"]),
                "armourPct": int(constants["armorPct"]),
                "gameModes": modes,
                "player": unit_filter(
                    {"allowed": [trait]} if trait else None,
                    {"allowed": [faction]} if faction else None,
                    None,
                ),
            })
        elif modifier["type"] != "battleEnvironmentAbility":
            raise ValueError(f"unhandled modifier {modifier['type']}")
        # Other battle environment abilities (Squig Smash and Warp Surge summons) are not served.
    return result


def variant(path, variant_key, scope):
    data = json.loads(path.read_text(encoding="utf-8"))
    tiers_raw = data["tiers"]
    unknown = set(tiers_raw) - set(TIER_ORDER)
    if unknown:
        raise ValueError(f"{path.name}: unknown tier keys {sorted(unknown)}")
    keys = [key for key in TIER_ORDER if key in tiers_raw]
    first = tiers_raw[keys[0]]
    for key in keys[1:]:
        for field in ("trackers", "modifiers"):
            if tiers_raw[key]["liveEventConfig"].get(field) != first["liveEventConfig"].get(field):
                raise ValueError(f"{path.name}: tier '{key}' {field} differ from tier '{keys[0]}'")
    result_tiers = []
    for key in keys:
        milestones, repeat = tier(tiers_raw[key])
        result_tiers.append({"tier": key.capitalize(), "milestones": milestones, "repeat": repeat})
    return {
        "variantKey": variant_key,
        "scope": scope,
        "tiers": result_tiers,
        # Trackers and modifiers are identical across power tiers (checked above); take the first.
        "trackers": trackers(first),
        "modifiers": modifiers(first),
    }


def main():
    v1_root = pathlib.Path(sys.argv[1])
    data_dir = v1_root / "src/fsd/4-entities/homescreen_events/data"
    by_definition = {}
    for stem, (definition_id, variant_key, scope) in SOURCES.items():
        by_definition.setdefault(definition_id, []).append(variant(data_dir / f"{stem}.json", variant_key, scope))
    output = [
        {"definitionId": definition_id, "variants": sorted(variants, key=lambda v: (v["variantKey"] != "default", v["variantKey"]))}
        for definition_id, variants in sorted(by_definition.items())
    ]
    target = pathlib.Path("src/TacticusPlanner.GameCatalog/Data/events/home-screen-event-scoring.json")
    target.write_text(json.dumps(output, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {len(output)} definitions to {target}")


if __name__ == "__main__":
    main()
