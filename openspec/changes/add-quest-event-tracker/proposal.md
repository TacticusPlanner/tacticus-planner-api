# Proposal

## Why

Quest Events (the catalog's `quest` definition) run every few weeks (Cyrus in August, Nubari now, Kîmm on 11-11), and the calendar already lists them. But the catalog has no battle data for them, so the client cannot show a quest's 17 battles, score them, or build teams for them. V1 ships five quests as a static `hero-quests.json`, and V2 has no copy. The 17-crate reward track is not in the catalog either. The companion apps change `add-quest-event-tracker` is the first client: the Dailies > Quest tab reads both datasets.

## What Changes

- New raw source `Data/quests/quest-events.json`, the last 3 quests (Cyrus, Nubari, Kîmm; 51 battles) of V1's `4-entities/quests/data/hero-quests.json`, copied unchanged. The older Sekhetar Robot and Ramus quests are dropped, as Severyn decided.
- New served dataset **`quest-events`**: one record per featured hero, with its allowed and enemy factions and its 17 battles. Each battle has its tier, its position in that tier, board id, spawn set, **deploy slots**, Lightning Victory turns, enemies (grouped into `{ id, ladderIndex, count }` like `lre-battles`), objectives with scores, and its upgrade drop with odds. Deploy slots follow Severyn's fixed rule per tier position (Common, Uncommon and Epic 3, 4, 5; Rare 3, 3, 3, 4, 5; Legendary 5, 5; Mythic 5; Nubari's in-game screens show Legendary 1 at 5). Each battle also has an optional `power`, the game's enemy power, recorded for Nubari's 17 battles from the in-game screens.
- New raw and served dataset **`quest-crates`**: the 17-crate point track (wiki QuestCrates) with structured rewards. It is shared by every quest.
- Load-time validation: 17 battles split 3/3/5/3/2/1, ids resolve (character, factions, NPCs, upgrades), objective types are known, and every quest totals exactly 8,800 points, which is crate 17's threshold.
- No `SchemaVersion` bump: two new datasets, no existing shape changes.

Out of scope: older quests (Sekhetar Robot, Ramus, and Cezare, which V1 never had). A quest occurrence without a record shows "No battle data for this quest yet" in the client. Quest tokens and progress are not in the game API (confirmed), so nothing is synced.

## Capabilities

### New Capabilities

- `quest-events-dataset`: the served `quest-events` and `quest-crates` shapes, how they are built from the raw files, the deploy-slot rule, enemy grouping and ladder index, and the load-time checks.

### Modified Capabilities

None.

## Impact

- Companion apps change: `add-quest-event-tracker` in `tacticus-planner-apps` (zod schemas, sync, the Quest tab). This API half applies first. Later apps-only changes (`add-quest-event-rewards`, `add-quest-event-teams`, `add-quest-event-clear-estimates`) read the same datasets and need no API work.
- `src/TacticusPlanner.GameCatalog`: `Models/GameCatalogDatasets.cs` (two raw keys, two served keys), new `Models/QuestEvents.cs`, `Denormalization/QuestEventsDenormalizer.cs`, `Validation/QuestEventsValidation.cs`, raw `Data/quests/quest-events.json` and `Data/quests/quest-crates.json`.
- `src/TacticusPlanner.Api/Features/GameCatalog/`: two `ServedDatasetEndpoint` endpoints; the OpenAPI artifact regenerates.
- Tests: `QuestEventsDenormalizerTests`, validation tests, the manifest snapshot (`GameCatalogSnapshotTests…verified.txt`).
- `.agents/skills/game-catalog-data/SKILL.md` and its `.claude` mirror describe the two datasets.
- No EF Core migration, no infra change.
