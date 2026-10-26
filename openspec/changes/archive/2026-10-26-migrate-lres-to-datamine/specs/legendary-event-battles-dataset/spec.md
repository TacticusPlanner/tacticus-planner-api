## MODIFIED Requirements

### Requirement: Battle records are copied from the raw event files

Each record SHALL be built from the battle of its event's datamine battle set (`battles/battleSets.json`, the set its `legendaryHeroEvent.json` lane names; lane 1 is alpha): `mapId` from `boardId`, `number` from `battleNr`, `power`, `tier` and `disallowedFactions` as stored, and `waves` with each wave's `round`, `power` and enemies. Wave enemies SHALL count each `"{npcId}:{N}"` army entry once per `(id, N)` pair, in order of first appearance. `id`, `lreId` and `track` are added. Records SHALL be ordered by event id, then alpha, beta, gamma, then the game's battle order. Every wave enemy `id` SHALL exist in the served `npcs` dataset, or loading fails.

#### Scenario: Values pass through

- **GIVEN** the Lysander Alpha battle 7 wave at round 1 spawns seven `orksNpc7Squig:24`
- **WHEN** the catalog loads
- **THEN** the served `astarLysander-alpha-7` record carries the enemy `{ id: "orksNpc7Squig", ladderIndex: 24, count: 7 }`
