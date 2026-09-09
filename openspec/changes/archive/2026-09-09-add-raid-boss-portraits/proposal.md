## Why

The Raid Bosses library detail (`tacticus-planner-apps`) wants to render a real
portrait for every field npc shown in an encounter. V1 keyed those portraits off
each unit set's `questUnitId` (the canonical npc that unit set represents), but
the served `raid-bosses` projection drops that field today — it is present on the
raw `GameCatalogRaidBossRawUnitSet` and never copied into
`GameCatalogRaidBossView`. Without it the client can only guess a field-npc
portrait from a fuzzy name match.

This is the backend half of the cross-repo pair; the companion apps change is
`add-raid-boss-portraits` in `tacticus-planner-apps` (portrait asset maps + icon
helpers). The API half applies first.

## What Changes

- Add a nullable `questUnitId` (string) to the served `raid-bosses` per-unit
  projection (`GameCatalogRaidBossView`), sourced verbatim from the raw unit
  set's `QuestUnitId`. Omitted from the payload when the source omits it
  (`JsonIgnore` when null), consistent with the other optional projection fields.
- The `RaidBossDenormalizer` copies the raw value through; no new validation
  rule (an absent `questUnitId` is valid — not every unit set has one).
- The served `raid-bosses` canonical-JSON hash changes, so the catalog manifest
  snapshot is regenerated.

No new raw dataset, no schema/EF migration, no game-version bump, no endpoint
contract change beyond the payload field.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `raid-bosses-dataset`: the served per-unit projection gains one structural id
  field (`questUnitId`); a new requirement states it is served when present and
  carries no display resolution.

## Impact

- `src/TacticusPlanner.GameCatalog/Models/RaidBosses.cs` —
  `GameCatalogRaidBossView` gains `QuestUnitId`.
- `src/TacticusPlanner.GameCatalog/Denormalization/RaidBossDenormalizer.cs` —
  maps `raw.QuestUnitId` onto the view.
- `tests/TacticusPlanner.Api.Tests/GameCatalogSnapshotTests` — manifest snapshot
  `.verified.txt` regenerated (served `raid-bosses` hash changes).
- `tests/TacticusPlanner.GameCatalog.Tests` — denormalizer coverage asserts
  `questUnitId` round-trips and is absent when the source omits it.
- Consumers: `tacticus-planner-apps` `add-raid-boss-portraits` reads the new
  field through the game-catalog package query surface.
