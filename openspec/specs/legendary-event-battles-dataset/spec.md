# legendary-event-battles-dataset Specification

## Purpose
Defines the served `lre-battles` dataset: one record per Legendary Event battle with its map, power and waves, derived from the raw event files, including what a wave enemy's `ladderIndex` means.

## Requirements

### Requirement: Each served battle record has a fixed shape

The served `lre-battles` dataset SHALL be an array of records, one per battle of every served event and lane, with exactly these fields. The server sends ids and numbers only; the client resolves names, portraits and map images.

```
{
  id: string,                    // "{lreId}-{track}-{number}"
  lreId: string,                 // the event unit's snowprint id, as in lres.id
  track: "alpha" | "beta" | "gamma",
  mapId: string,                 // map asset id, e.g. "LE1_26"
  number: int,                   // 1-based battle number in the lane
  power: int,
  tier: int,
  disallowedFactions: string[],  // faction ids
  waves: { round: int, power: int, enemies: { id: string, ladderIndex: int, count: int }[] }[]
}
```

#### Scenario: Lysander Alpha battle 1

- **WHEN** a client fetches `lre-battles`
- **THEN** it contains a record with id `astarLysander-alpha-1`, `lreId` `astarLysander`, `track` `alpha`, `mapId` `LE1_26`, `number` 1, `power` 994, and a first wave `{ round: 1, power: 147, enemies: [{ id: "orksNpc1Grot", ladderIndex: 4, count: 7 }] }`

#### Scenario: No stars field

- **WHEN** any wave enemy of any served record is read
- **THEN** it has `id`, `ladderIndex` and `count` and no `stars` field

### Requirement: Battle records are copied from the raw event files

Each record SHALL be built from the battle of its event's datamine battle set (`battles/battleSets.json`, the set its `legendaryHeroEvent.json` lane names; lane 1 is alpha): `mapId` from `boardId`, `number` from `battleNr`, `power`, `tier` and `disallowedFactions` as stored, and `waves` with each wave's `round`, `power` and enemies. Wave enemies SHALL count each `"{npcId}:{N}"` army entry once per `(id, N)` pair, in order of first appearance. `id`, `lreId` and `track` are added. Records SHALL be ordered by event id, then alpha, beta, gamma, then the game's battle order. Every wave enemy `id` SHALL exist in the served `npcs` dataset, or loading fails.

#### Scenario: Values pass through

- **GIVEN** the Lysander Alpha battle 7 wave at round 1 spawns seven `orksNpc7Squig:24`
- **WHEN** the catalog loads
- **THEN** the served `astarLysander-alpha-7` record carries the enemy `{ id: "orksNpc7Squig", ladderIndex: 24, count: 7 }`

### Requirement: A wave enemy's ladderIndex is a 1-based stat ladder index

`ladderIndex` SHALL be the 1-based position of the enemy's row in its NPC's served `stats` ladder (V1's `N` in `"{npcId}:{N}"`), not a star count. The server SHALL NOT check it against the ladder length.

Assumptions:
- The served `npcs` ladder keeps the raw order V1 indexed.
- Four Lysander boss entries point past their ladder today.

#### Scenario: Grot at index 4

- **GIVEN** `orksNpc1Grot`'s served ladder whose row at position 4 (zero-based 3) is rank 2, stars 1
- **WHEN** a client reads `{ id: "orksNpc1Grot", ladderIndex: 4 }`
- **THEN** the enemy's stats are that row

#### Scenario: Index past the ladder loads

- **GIVEN** `templBossHelbrecht` has 2 ladder rows and Lysander Beta battle 13 serves it with `ladderIndex` 3
- **WHEN** the catalog loads
- **THEN** loading succeeds and the record is served unchanged
