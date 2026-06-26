"""Round-11 catalog transform (runs on the post-round-10 Data tree).

Inject v1's static LRE battle/enemy data into the 3 raw lres files. Source: the v1 repo's
`new-le-battle-data.json` (`legendaryEvents`, ids 10-14). For each v2 `Data/lres/lres-*.json` we match
the v1 event by the raw file's `id` and inject a per-track `battles` array:

    battles[] = { mapId, number, power, tier, disallowedFactions, waves[] }
    waves[]   = { round, power, enemies[] }
    enemies[] = { id, stars, count }   # parsed from v1 "npcId:stars" wave entries, duplicates aggregated

Per-battle `objectives` are intentionally dropped (they duplicate the track `unitsRestrictions`). The
per-track `defeatAll` points array already lives in the raw lres json (surfaced by the model change, not
here). The v1 file is read once; the resulting data is committed into the v2 raw lres files.

Regenerates the source manifest with gameVersion 1.40 and schemaVersion 10 (served lres shape grows).
Idempotent + self-validating: 18 battles per track and every enemy id resolves in the npcs source.
"""
import json
import os
import glob
import collections

API = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(API, "src", "TacticusPlanner.Catalog", "Data")
LRES_DIR = os.path.join(DATA, "lres")
NPCS_DIR = os.path.join(DATA, "npcs")
V1_BATTLES = r"D:/repos/tacticus/v1/tacticusplanner/src/fsd/1-pages/plan-lre/new-le-battle-data.json"

GAME_VERSION = "1.40"
SCHEMA_VERSION = 10
TRACKS = ("alpha", "beta", "gamma")


def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def write(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


def parse_wave_enemies(entries):
    """["npcId:stars", ...] -> [{id, stars, count}], duplicates aggregated, first-seen order."""
    counts = collections.OrderedDict()
    for entry in entries:
        npc_id, _, stars = entry.partition(":")
        key = (npc_id, int(stars))
        counts[key] = counts.get(key, 0) + 1
    return [{"id": npc_id, "stars": stars, "count": count}
            for (npc_id, stars), count in counts.items()]


def convert_battle(battle):
    return {
        "mapId": battle["mapId"],
        "number": battle["number"],
        "power": battle["power"],
        "tier": battle["tier"],
        "disallowedFactions": battle["disallowedFactions"],
        "waves": [
            {"round": wave["round"], "power": wave["power"], "enemies": parse_wave_enemies(wave["enemies"])}
            for wave in battle["waves"]
        ],
    }


# ---- collect npc ids (for validation) --------------------------------------------------------
npc_ids = set()
for path in glob.glob(os.path.join(NPCS_DIR, "*.json")):
    for npc in load(path).get("npcs", []):
        npc_ids.add(npc["id"])

# ---- index v1 battle events by id ------------------------------------------------------------
v1_events = {str(event["id"]): event for event in load(V1_BATTLES)["legendaryEvents"]}

# ---- inject battles into each raw lres file --------------------------------------------------
events = 0
total_battles = 0
for path in sorted(glob.glob(os.path.join(LRES_DIR, "lres-*.json"))):
    lre = load(path)
    event = v1_events.get(str(lre["id"]))
    assert event is not None, ("no v1 battle data for lre id", lre["id"], path)
    for track_id in TRACKS:
        battles = [convert_battle(b) for b in event[track_id]["battles"]]
        assert len(battles) == 18, (path, track_id, len(battles))
        for battle in battles:
            assert battle["waves"], (path, track_id, battle["number"])
            for wave in battle["waves"]:
                assert wave["enemies"], (path, track_id, battle["number"], wave["round"])
                for enemy in wave["enemies"]:
                    assert enemy["id"] in npc_ids, ("unresolved enemy", enemy["id"], path)
        lre[track_id]["battles"] = battles
        total_battles += len(battles)
    write(path, lre)
    events += 1


# ---- regenerate source manifest (schemaVersion 10) -------------------------------------------
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
    f"lre battles injected: events={events} battles={total_battles} (tracks={events * 3}) | "
    f"npc ids={len(npc_ids)} | schemaVersion={SCHEMA_VERSION} gameVersion={GAME_VERSION}"
)
