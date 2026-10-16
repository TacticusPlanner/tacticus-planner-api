# Spec Delta

## ADDED Requirements

### Requirement: The catalog serves finished Legendary Events that have full V1 data

The `lres` dataset SHALL serve every Legendary Event whose raw `lres-*.json` file is listed in `GameCatalogDatasets.LreEvents`. That list SHALL include, besides the active and upcoming events, the finished events Dante (`bloodDante`, V1 id 10), Trajann (`custoTrajann`, V1 id 11) and Lucius (`emperLucius`, V1 id 12). Their raw files SHALL use the existing raw shape and SHALL be derived from the V1 generated data as follows:

- `id` = the V1 numeric id; `unitSnowprintId` = the served id above; `finished` = true; `nextEventDate` / `nextEventDateUtc` from V1 `lre-event-dates.ts` (none for Dante, 17 May 2026 for Trajann, 26 July 2026 for Lucius);
- `regularMissions`, `premiumMissions`, `pointsMilestones`, `chestsMilestones`, `shardsPerChest` and `progression` from the V1 per-character file (`10-dante.json`, `11-trajann.json`, `12-lucius.json`);
- per lane (`alpha`, `beta`, `gamma`) from that id's `new-le-battle-data.json` entry, as V1 `new-format-adapter.ts` builds it: `name`, `killPoints`, `battlesPoints`, `defeatAll`, `allowedUnitsFilter` (from the lane's `disallowedFactions` and alliance rule), `unitsRestrictions` (one per bonus objective in V1 order, `index` 0..4, with its filter `kind`, `target`, `exclude` and `points`) and `battles` (number, power, tier, waves of enemy npc ids).

The served projection SHALL be the existing `GameCatalogLreView` with no new fields: `id` (snowprint id), `finished`, `eventStageStartDatesUtc` (zero or one ISO 8601 UTC string from `nextEventDateUtc`), `regularMissions`, `premiumMissions`, `rewards { pointsMilestones, chestsMilestones, progression, shardsPerChest }`, and `alpha` / `beta` / `gamma` each with `killPoints`, `battlesPoints`, `defeatAll`, `allowedUnitsFilter`, `unitsRestrictions`, `battleIds` and the derived `availableUnitIds`. Their battles SHALL be served in `lre-battles` with ids `{lreId}-{track}-{number}`. Every wave enemy id SHALL resolve to a served npc; a missing npc SHALL fail catalog load. V1 events with no V1 battle data (ids 1–9) SHALL NOT be served. Display names stay the client's.

#### Scenario: Dante is served as finished

- **WHEN** the client reads `lres`
- **THEN** it contains `bloodDante` with `finished` true, `eventStageStartDatesUtc` `[]`, three lanes of 18 battles each, five `unitsRestrictions` per lane with indexes 0–4, and `lre-battles` contains `bloodDante-alpha-1` … `bloodDante-gamma-18`

#### Scenario: Trajann keeps its last known date

- **WHEN** the client reads `custoTrajann`
- **THEN** `finished` is true and `eventStageStartDatesUtc` is `["2026-05-17T00:00:00Z"]`, so the client's lifecycle rule archives it

#### Scenario: The V1 id resolves to the served id

- **WHEN** `GameCatalogLreLookups.ServedLreIdForV1Id(12)` is called
- **THEN** it returns `emperLucius`

#### Scenario: Older V1 events stay out

- **WHEN** the client reads `lres`
- **THEN** there is no entry for Kharn (`worldKharn`, V1 id 7) or any other V1 id 1–9

#### Scenario: Missing npc fails the load

- **GIVEN** a wave in `lres-blooddante.json` names an enemy id absent from every `npcs-*.json`
- **WHEN** the catalog loads
- **THEN** loading fails naming the event file and the enemy id
