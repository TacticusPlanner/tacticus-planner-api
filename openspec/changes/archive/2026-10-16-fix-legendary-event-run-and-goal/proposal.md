# Proposal

API half of the paired change `fix-legendary-event-run-and-goal` (companion: `tacticus-planner-apps`, same name). This half applies first.

## Why

Between runs the Tacticus API sends no run number: on 2026-10-16 `currentEventRun` was `null` for `votanUthar`, `tauFarsight` and `astarLysander`, all of which had synced progress. The served `lres` dataset carries at most one run start per event (the next one), and the V1 field that told which run that date belongs to (`eventStage`) is dropped when the raw file is loaded. So the client cannot tell which run is next: the event page says "for when run 1 starts" on Uthar after runs 1 and 2 ended, plans tokens for runs that are over and counts their missions again in the reward outlook. The client already derives lifecycle from `eventStageStartDatesUtc`; it only needs that array to say which run each start belongs to.

## What Changes

- Each raw `lres-*.json` gains `runStartDatesUtc`: the start instants of the event's runs that have been announced, in run order (index 0 is run 1), at most three, RFC 1123 or ISO 8601 UTC like `nextEventDateUtc` today.
- The denormalizer serves `eventStageStartDatesUtc` from `runStartDatesUtc` as ISO 8601 UTC strings, so the array index is the run number minus one. Events with no announced run serve an empty array.
- The catalog loader rejects a raw file whose run starts are unparseable, number more than three, are not strictly ascending, or are closer together than the 7-day run length.
- The four embedded events get their run dates (Farsight and Uthar two runs each, Lysander and Fabius Bile one); the single-date fallback is removed so a lone date is never ambiguous.
- No served shape change (`eventStageStartDatesUtc` stays `string[]`), no new endpoint, no database change.

## Capabilities

### New Capabilities

- `legendary-event-run-schedule`: the served run start dates of a Legendary Event (index = run − 1), how they are derived from the raw file, and the rules the loader enforces.

### Modified Capabilities

None.

## Impact

- `src/TacticusPlanner.GameCatalog`: `Models/Lres.cs` (raw record), `Denormalization/LreDenormalizer.cs` (`BuildEventStageStartDates`), `Validation` (new run-start check), `Data/lres/lres-*.json` (four files).
- Tests: `tests/TacticusPlanner.GameCatalog.Tests/LreDenormalizerTests.cs` and the manifest snapshot test (the `lres` dataset hash changes).
- `.agents/skills/game-catalog-data/SKILL.md`: document `runStartDatesUtc` and the V1 `lre-event-dates.ts` mapping.
- The apps half reads the array to derive the current and next run (`legendary-event-run-resolution`) and fixes the ascension goal for owned units.
- Release metadata: content-only change, so `SchemaVersion` stays 5 and the per-dataset hash carries the update; the release `Version` tag moves with the data.
- Data supplied by the product owner on 2026-10-16: Farsight 2025-12-28 and 2026-06-21, Uthar 2026-03-08 and 2026-10-04, Lysander 2026-08-30, Fabius Bile 2026-11-08 (V1's hand-kept `lre-event-dates.ts` is stale).
