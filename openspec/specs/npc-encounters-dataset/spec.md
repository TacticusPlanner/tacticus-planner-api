# npc-encounters-dataset Specification

## Purpose

Defines the backend game catalog's served `npc-encounters` dataset: for every NPC, every place a player
fights it as an enemy, so a client can show where an NPC is encountered and filter NPCs by game mode
without loading every battle dataset.

## Requirements

### Requirement: The catalog serves an `npc-encounters` dataset

The game catalog SHALL serve a dataset keyed `npc-encounters` as a plain array at
`GET /api/v1/game-catalog/npc-encounters`, without authentication, in the shared dataset envelope and
with a manifest hash entry. The dataset SHALL be non-empty. The server SHALL send ids, enum values and
numbers only: no display names, labels, icon paths, links, schedules or availability flags. The client
resolves all of those.

#### Scenario: Anonymous fetch

- **WHEN** an anonymous client requests `GET /api/v1/game-catalog/npc-encounters`
- **THEN** it receives the dataset envelope with a non-empty array, and the manifest lists
  `npc-encounters` with its hash

### Requirement: Each encounter record has a fixed shape

Every record SHALL carry exactly these fields:

```
NpcEncounter {
  npcId:   string,                // an npcs.id
  source:  "campaign" | "campaignEvent" | "legendaryEvent" | "questEvent"
         | "incursion" | "guildRaid" | "survival" | "onslaught",
  groupId: string,                // the campaign, event, boss or season; see the source table
  battles: NpcEncounterBattle[]   // non-empty
}

NpcEncounterBattle {
  battleId:  string | null,       // id in the source's served battle dataset; null where it has none
  track:     string | null,       // campaignEvent: "Standard" | "Extremis"; legendaryEvent: "alpha" | "beta" | "gamma"; else null
  tier:      int | null,          // incursion: the served tier `index`; onslaught: the raw tier (1-based); else null
  number:    int | null,          // campaign / campaignEvent: nodeNumber; legendaryEvent: battle number; questEvent: battle number; else null
  challenge: bool,                // campaign / campaignEvent / incursion: the battle's challenge flag; else false
  role:      "enemy" | "boss" | "prime",  // guildRaid: "boss"/"prime" when the NPC is the encounter's own boss/prime; else "enemy"
  rounds:    int[]                // survival: the one round of this entry; onslaught: the wave rounds the NPC spawns in, ascending, distinct; else []
  count:     int | null           // how many of the NPC this place spawns: the battle's total across its waves; survival: that round's; onslaught: the most in any one wave of the tier; guildRaid: null
  levels:    NpcEncounterLevel[]  // count split by level, lowest first; onslaught: the most at that level in any one wave; guildRaid: []
}
```

`NpcEncounterLevel` is `{ servedIndex: int | null, count: int }`. `servedIndex` is the 0-based position in the
NPC's served `stats`, or `null` when the source's index points past the ladder. Indexes are read per source:
1-based for `legendaryEvent`, `questEvent`, `survival` and `onslaught`; 1-based and capped at the last row for
`incursion`; and for `campaign` / `campaignEvent` the `servedIndex` that `campaign-battles` now serves on each
`detailedEnemyTypes` entry.

There SHALL be one record per distinct `(npcId, source, groupId)`. Within a record, `battles` SHALL hold
no two identical entries.

#### Scenario: Every field is present

- **WHEN** any served record and any of its battles are read
- **THEN** every field above is present (nullable fields as `null`, `rounds` as `[]` when empty) and no
  other field is present

### Requirement: Encounters are derived per source by a stated rule

Each source SHALL be derived as below. An enemy is every NPC a battle's enemy side spawns. "Served view"
means the record as the catalog serves it in that dataset, so every `groupId` and `battleId` resolves
there.

| `source` | Derived from | `groupId` | Battle entry |
|---|---|---|---|
| `campaign` | each `campaign-battles` record whose `campaign-definitions` group has `releaseType` other than `event`, its `detailedEnemyTypes[].id` | the battle's `campaignGroupId` | `battleId` = battle `id`, `number` = `nodeNumber`, `challenge` = `challenge` |
| `campaignEvent` | the same for groups with `releaseType` `event` | the battle's `campaignGroupId` (`eventCampaignN`) | as `campaign`, plus `track` = the battle's `type` (`Standard` or `Extremis`) |
| `legendaryEvent` | each `lre-battles` record, every wave's `enemies[].id` | the record's `lreId` | `battleId` = record `id`, `track` = `track`, `number` = `number` |
| `questEvent` | each `quest-events` battle's `enemies[].id` | the quest's `featuredCharacterId` | `number` = battle `number` |
| `incursion` | each `incursions` tier battle's `enemies[].npcId` | the record's `mowId` | `battleId` = battle `id`, `tier` = tier `index`, `challenge` = `challenge` |
| `guildRaid` | `raid-bosses`: each boss / prime unit set's `questUnitId`; and each encounter's `fieldNpcIds`, resolved through that unit set's `questUnitId` | the encounter's (or the boss / prime's own) `unitSetId` | `role` = `boss` / `prime` for the unit set itself, `enemy` for a field unit; all else null / false / `[]` |
| `survival` | each `survival_*` battle set in `gameconfig/battles/battleSets.json`, every wave's `army` entries `"{npcId}:{N}"` | the battle set key (e.g. `survival_crescendo_01`) | one entry per wave `round` listing the NPC, `rounds` = that round, `count` = the times the `army` repeats the NPC |
| `onslaught` | `tierWaves` in `gameconfig/battles/onslaught.json`, each wave's `enemies` entries `"{npcId}:{N}"` | `onslaught` | one entry per `tier`, `rounds` = that tier's wave `round`s listing the NPC, `count` = the most in any one wave (a tier draws from a pool of waves, so they do not add up) |

Assumptions:
- An Onslaught tier draws each round's wave from a weighted pool, so an Onslaught entry means the NPC
  *can* appear in that tier and round, not that it always does. The dataset sends no weights.
- `legendaryEvent` covers the events `lre-battles` serves. Legendary Event battle sets the catalog
  does not serve are not indexed.
- A guild raid unit set without a `questUnitId` names no NPC and is skipped. Some primes are built on playable
  heroes: their `questUnitId` is a served character id (e.g. `GuildBoss7MiniBoss1AstraPrimarisPsy` to
  `astraPrimarisPsy`). Those are not NPCs and are skipped too.

#### Scenario: Legendary Event battle

- **GIVEN** the served `lre-battles` record `astarLysander-alpha-1` whose first wave spawns `orksNpc1Grot`
- **WHEN** `npc-encounters` is served
- **THEN** the `orksNpc1Grot` / `legendaryEvent` / `astarLysander` record has a battle
  `{ battleId: "astarLysander-alpha-1", track: "alpha", tier: null, number: 1, challenge: false, role: "enemy", rounds: [], count: 23 }`
  (`count` adds up all its waves)

#### Scenario: Quest Event battle

- **GIVEN** the served quest whose `featuredCharacterId` is `astarCyrus`, with battle 1 spawning
  `necroNpc1Warrior`
- **WHEN** `npc-encounters` is served
- **THEN** the `necroNpc1Warrior` / `questEvent` / `astarCyrus` record has a battle with `number` 1 and
  `battleId` null

#### Scenario: Event campaign battle carries its track

- **GIVEN** an event campaign group `eventCampaignN` whose `Extremis` challenge battle at node 3 spawns `X`
- **WHEN** `npc-encounters` is served
- **THEN** the `X` / `campaignEvent` / `eventCampaignN` record has a battle with that battle's `id`,
  `track` `Extremis`, `number` 3 and `challenge` true

#### Scenario: Guild raid field unit

- **GIVEN** a guild raid encounter against unit set `GuildBoss1Boss1TyranTervigonLeviathan` whose field
  unit `GuildBoss1Npc1TyranTermagantLeviathan` has `questUnitId` `tyranNpc3Termagant`
- **WHEN** `npc-encounters` is served
- **THEN** the `tyranNpc3Termagant` / `guildRaid` / `GuildBoss1Boss1TyranTervigonLeviathan` record has a
  battle with `role` `enemy`

#### Scenario: Survival rounds

- **GIVEN** `survival_crescendo_01` spawns `blackNpc3TraitorGuardsman` in rounds 1, 3, 5, 12, 18, 22, 28
  and 34
- **WHEN** `npc-encounters` is served
- **THEN** the `blackNpc3TraitorGuardsman` / `survival` / `survival_crescendo_01` record has one battle
  per round, with `rounds` `[1]`, `[3]` and so on to `[34]`, each with that round's `count`

#### Scenario: Onslaught tiers

- **GIVEN** `tyranNpc1Hormagaunt` is listed in Onslaught waves of tiers 1 to 21
- **WHEN** `npc-encounters` is served
- **THEN** the `tyranNpc1Hormagaunt` / `onslaught` / `onslaught` record has 21 battles, `tier` 1 to 21,
  each with the rounds of that tier's waves listing it and the most of it in any one wave as `count`

#### Scenario: Battle count

- **GIVEN** a Legendary Event battle spawns 2 `orksNpc1Grot` in one wave and 3 in another
- **WHEN** `npc-encounters` is served
- **THEN** that battle's entry in the `orksNpc1Grot` record has `count` 5

#### Scenario: One NPC at two levels in a battle

- **GIVEN** a Legendary Event battle spawns `orksNpc1Grot` at ladder indexes 4 and 5
- **WHEN** `npc-encounters` is served
- **THEN** that battle has two `levels`, `servedIndex` 3 and 4, whose counts add up to its `count`

#### Scenario: Guild raids carry no count

- **WHEN** `npc-encounters` is served
- **THEN** every `guildRaid` battle has `count` `null` and `levels` `[]`

### Requirement: Only enemy units are indexed, and only from the listed sources

A record SHALL be produced only for an NPC whose `npcs` `kind` is `unit` or `machineOfWar`. Enemies whose
`kind` is `object` (loot objects, board power-ups) SHALL be skipped. No source other than the eight above
SHALL be read. In particular, Sync PvP battle sets (`sync_pvp_*`) SHALL NOT be indexed. An NPC no
indexed battle names SHALL have no record. That means "no recorded encounter", not "not in the game".

#### Scenario: Loot object skipped

- **GIVEN** a quest battle whose enemies include a board power-up NPC of `kind` `object`
- **WHEN** `npc-encounters` is served
- **THEN** no record has that power-up's id as `npcId`

#### Scenario: Sync PvP variation not indexed

- **WHEN** `npc-encounters` is served
- **THEN** no record has `npcId` `tyranNpc1Hormagaunt_SyncPvp_Kronos`

### Requirement: Records and battles have a stable order

Records SHALL be ordered by `npcId` (ordinal), then `source` in the order the shape lists it (`campaign`
first, `onslaught` last), then `groupId` (ordinal). A record's `battles` SHALL follow the order of the
source: `campaign-battles` order for campaigns, `lre-battles` order for Legendary Events, quest battle
order, incursion tier then battle order, and ascending `tier` for Onslaught. So the dataset hash moves
only when the encounters do.

#### Scenario: Two loads hash alike

- **WHEN** the catalog loads twice from the same datamine
- **THEN** both serve byte-identical `npc-encounters` payloads with the same hash

### Requirement: Unresolvable encounters fail the load

Loading SHALL fail, naming `npc-encounters`, the source and the offending id, when an indexed enemy id
is not an `npcs` id, when a `guildRaid` unit set's `questUnitId` is present but is neither an `npcs` id nor a
served character id, when a
Survival or Onslaught entry is not `"{npcId}:{N}"`, or when the built dataset is empty.

#### Scenario: Hero-based prime skipped

- **GIVEN** the prime `GuildBoss7MiniBoss1AstraPrimarisPsy` whose `questUnitId` is the character `astraPrimarisPsy`
- **WHEN** `npc-encounters` is served
- **THEN** no record has `npcId` `astraPrimarisPsy`, and the load succeeds

#### Scenario: Unknown Survival enemy

- **GIVEN** a `survival_*` wave whose `army` lists `notAnNpc:3`
- **WHEN** the catalog loads
- **THEN** loading fails naming `npc-encounters`, `survival` and `notAnNpc`
