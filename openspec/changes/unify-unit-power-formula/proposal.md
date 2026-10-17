## Why

V1 now computes a unit's power with the game's own formula (V1 commit `cdeea3a`, `4-entities/unit/character-power`). It matches the in-game power score exactly: worldKharn 936,827, orksWarboss 1,037,811, a maxed tyranBiovore 1,040,687. V2 still uses V1's older heuristic (`characterCombatPower`), so its power figures and its Legendary Event clear-depth estimate are on a different scale from the game's battle power. Most inputs the formula reads are already in the catalog: unit base stats, weapons, movement, traits, ability ids, rank-up upgrade ids, equipment level stats and upgrade stat types. The rest are not served today: the stat increase of each rank-up upgrade slot, each unit's power multiplier, the progression multipliers and the power tables (damage-profile, trait and ability modifiers, ability power curves).

## What Changes

- Add a raw embedded `unit-power.json`, ported from V1's bundled GameConfig slice (`character-power-units.json`), with only the fields the formula reads.
- Serve a new dataset `unit-power`. It holds the global power tables, the hero and MoW progression multipliers, and one record per catalog character and MoW with its power multiplier and per-rank upgrade stat increases. The record is validated at load against the units, upgrades and abilities the catalog already has.
- No change to `units-*`, `npcs-*`, `equipment-*` or `upgrades-*`. The client reads the existing equipment stats, upgrade stat types and unit records alongside `unit-power`.
- Bump `GameCatalogRelease.SchemaVersion` (a dataset is added) and `Version`.
- Update the `game-catalog-data` skill: how to refresh `unit-power.json` from V1 on each game patch, and that a new character or MoW needs its `unit-power` entry.

Companion change: **`unify-unit-power-formula`** in `tacticus-planner-apps`. It ports the formula to `@workspace/game-domain` and uses it on the Library unit pages, the NPC library, Legendary Events and Dailies. This API half applies first.

## Capabilities

### New Capabilities

- `unit-power-dataset`: the served `unit-power` dataset, covering its shape, how it is derived from the raw file, and load validation.

### Modified Capabilities

(none)

## Impact

- `src/TacticusPlanner.GameCatalog`: new raw `Data/unit-power.json`, model `Models/UnitPower.cs`, denormalizer, validator, `GameCatalogDatasets.UnitPower`, manifest entry.
- `tests/TacticusPlanner.GameCatalog.Tests`: denormalization, validation and manifest snapshot coverage.
- One new endpoint, `/api/v1/game-catalog/unit-power` (one per served dataset, like the others), so the OpenAPI artifact regenerates. No EF Core schema or migration change.
