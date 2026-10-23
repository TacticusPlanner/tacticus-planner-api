# Design

## Context

- `event-definitions.json` has `quest` (`type: Quest`, 72 h, 24 h lead, 24 h claim window, `featuredCharacterId` required). `event-occurrences.json` has four quests: Cezare (`bloodTerminator`), Cyrus (`astarCyrus`), Nubari (`astarEradicator`, running 2026-10-21 to 10-24) and Kîmm (`votanChampion`).
- V1 `hero-quests.json` has 5 quests and 85 battles. Every enemy id, power-ups included, is in the served `npcs` dataset (checked). Every quest totals 8,800 points.
- The companion apps change `add-quest-event-tracker` reads both new datasets. The later apps changes (rewards, teams, clear estimates) read them too, without new fields.

## Goals / Non-Goals

**Goals:**

- One record per quest with everything the client needs to show, score and plan a battle.
- Keep the raw file a straight V1 copy, so a refresh is a copy.

**Non-Goals:**

- Battle power. The client computes it from NPCs, as decided in the exploration.
- Quest tokens or progress, which the game API does not expose.
- Board images. Those are client assets.

## Decisions

### D1. Two datasets: per-quest battles and one shared crate track

`quest-events` inlines its battles because a quest is small (17 battles of about 10 enemies) and the client always reads a quest whole. This is unlike `lre-battles`, which is split out because it has 3 lanes and waves. `quest-crates` is shared because the crate track does not change between quests. That mirrors `lre-common`.

- _Alternative:_ add the crates to each quest record. That was rejected because it duplicates one table five times, and a wiki update would touch every record.

### D2. Deploy slots come from the rule, not from `spawnPointsSet`

Severyn gave the per-position slot table. `spawnPointsSet` ranges from 1 to 13 and looks like an index into a board's spawn sets, not a count. So the denormalizer derives `deploySlots` from tier and position, and `spawnPointsSet` passes through for later use.

### D3. Enemies are grouped like `lre-battles`

V1 lists one entry per unit. Grouping by `(id, index)` into `count` gives the client the same enemy shape as LE, so the shared battle-preview widget in the apps change renders both. A null index is a power-up, and V1 reads it as 1.

### D4. The 8,800 check ties the two datasets together

The validator compares each quest's maximum with crate 17's `points`. A data entry error in either file then fails at load, not in a player's crate projection.

### D5. Raw files live under `Data/quests/`

The loader matches by leaf file name, so the folder only groups the two files.

## Risks / Trade-offs

- [The crate table on the wiki is out of date] → it is one raw file; the 8,800 check guards the total, not the rewards.
- [A future quest has a different tier split] → the validator fails loudly, and the rule moves to data then.
- [The deploy-slot rule changes] → it is one table in the denormalizer and one spec table.

## Migration Plan

Additive. Clients that do not know the datasets ignore them in the manifest. This half merges before the apps half.

## Open Questions

- **Cezare's battles.** V1 has no data for `bloodTerminator`. Add it when a source (datamine or wiki) is available; the validator then covers it.
- **Occurrence windows.** Cezare and Cyrus are authored as 7-day windows (08-09 to 08-16, 08-30 to 09-06), and Nubari and Kîmm as 72 h. The definition says 72 h. The two older ones look wrong, but they are in the past and do not affect this change.
