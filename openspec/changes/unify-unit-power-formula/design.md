## Context

See proposal.md for why. The client's power formula (companion apps change `unify-unit-power-formula`) is a port of V1's `calculateRosterCharacterPower` / `calculateRosterMowPower`. Three inputs are missing from the catalog today: per-slot upgrade stat increases, unit power multipliers, and the progression and power tables. V1 bundles them as a trimmed `clientGameConfig` slice (`src/fsd/4-entities/unit/character-power/data/`, about 1.3 MB across three files). Of those, `items` and `upgrades` duplicate data the catalog already serves: all 217 equipment items have identical level stats after a key rename (`critDmg`→`critDamage`, `fixedArmor`→`armor`), and every upgrade's `statType` matches the catalog `stat`. Only `units` adds anything.

Contract surface shared with the apps change: the served `unit-power` dataset (shape in `specs/unit-power-dataset/spec.md`), read through the existing manifest and dataset routes.

## Goals / Non-Goals

**Goals:**

- Serve exactly the inputs the formula reads that the catalog does not already serve, in one canonical record.
- Fail loudly at load when a catalog unit has no power data, so a game patch that adds a hero cannot ship a silent 0 or a wrong figure.

**Non-Goals:**

- Computing power on the server. The client computes it from progression the user picks on the page, so the formula lives in `@workspace/game-domain`.
- NPC crit, block, extra abilities or power multipliers. V1's datamine has none of these, and no GameConfig NPC slice exists in V1 to port. See Risks.

## Decisions

- **One new dataset instead of extra fields on `units-*`.** All the fields come from one source (the GameConfig slice), are refreshed together every patch, and are read only by the power formula. Putting the per-unit fields on the unit records would split one refresh across 23 faction files and grow every unit payload. The alternative (`units-*` gains `powerMultiplier` and `rankUpUpgrades[].statIncreases`) puts each fact next to its unit but forces two parallel refresh paths. The trade-off is that the client joins `unit-power.units` to `units-*` by id, which it already does for every other per-unit dataset.
- **Lists, not maps, for the modifier tables.** Every served dataset here uses arrays of records with a stable sort for hashing. The client builds lookup maps.
- **No per-unit progression overrides.** All 16 V1 overrides (`heroProgressionStepsPerUnit`) differ from the default only in `costs` and `ascensionCosts`. The validator does not need to enforce this, because overrides are simply not read. A future override that changes a multiplier would show up as a parity-test failure in the apps change.
- **Raw file is V1's shape.** `unit-power.json` keeps V1's `character-power-units.json` keys, trimmed to `lineup.{id}.{powerMultiplier, upgradesStatIncrease}` plus the global tables. Refreshing it each patch is then a mechanical copy and trim, which the `game-catalog-data` skill documents.
- **Denormalization** (`UnitPowerDenormalizer`): it maps the raw file to the served shape, sorts the lists ordinally, defaults `powerMultiplier` to 100, truncates `upgradesStatIncrease` to the catalog row count, and emits `[]` for MoWs. Raw units that are not in the catalog are dropped.

Manifest impact: one new entry `unit-power`. The manifest snapshot test and `GameCatalogRelease.SchemaVersion` (5 → 6) change. It is served at `/api/v1/game-catalog/unit-power` by a new `GetGameCatalogUnitPowerEndpoint` (one endpoint per served dataset), so `artifacts/openapi` regenerates. No EF Core migration.

## Risks / Trade-offs

- [NPC power is approximate] → I ran V1's formula on the catalog NPC stat rows. It reproduces the game's LE wave power within rounding for 656 of 1,292 waves. Waves with any of 66 NPC ids, mostly bosses, are off by up to 1.5×. This dataset does not close that gap. The apps change keeps catalog battle power for clear depth (it is the game's own number) and labels NPC power where it is shown.
- [V1 slice goes stale] → the load-time validation fails on any catalog unit missing from the raw file, and the skill update makes the refresh a listed step.
- [Duplicate truth with V1] → V1 keeps its own bundled copy. Both are refreshed from the same extraction, and the apps parity test (worldKharn, orksWarboss, tyranBiovore) catches drift.
