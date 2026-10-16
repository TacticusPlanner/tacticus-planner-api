# Tasks

Paths are under `src/TacticusPlanner.GameCatalog` unless stated. Build and test commands: see this repo's AGENTS.md (`dotnet test tests/TacticusPlanner.GameCatalog.Tests`).

## 1. Raw model, denormalizer and validation

- [x] 1.1 Add `RunStartDatesUtc` (`IReadOnlyList<string>`, absent reads as empty) to `GameCatalogLre` in `Models/Lres.cs` and remove `NextEventDateUtc`; update `Denormalization/LreDenormalizer.cs` so `BuildEventStageStartDates` normalises every entry to `yyyy-MM-ddTHH:mm:ssZ` in order (spec: served run start dates are indexed by run; the served array is derived from the raw run starts). Verify: `LreDenormalizerTests` has a case where a two-entry raw schedule serves two ISO strings in order, and one where an absent or empty field serves `[]`.
- [x] 1.2 Add `ValidateLreRunStarts` beside `ValidateLreRewards` in `Validation` (code `InvalidRunStartDates`; more than 3 entries, unparseable entry, not strictly ascending, spacing under 7 days; the error names the event id, source file and entry index) and call it from `GameCatalogValidator`. Verify: unit tests cover each scenario of the spec's "loader rejects invalid run start dates" requirement (four starts, overlap at 5 days, "soon", out of order) plus a valid 0-, 1-, 2- and 3-entry file.

## 2. Embedded data and catalog docs

- [x] 2.1 Write `runStartDatesUtc` into `Data/lres/lres-votanuthar.json` (`Sun, 08 March 2026 00:00:00 GMT`, `Sun, 04 October 2026 00:00:00 GMT`), `lres-taufarsight.json` (`Sun, 28 December 2025 00:00:00 GMT`, `Sun, 21 June 2026 00:00:00 GMT`), `lres-astarlysander.json` (`Sun, 30 August 2026 00:00:00 GMT`) and `lres-lostabile.json` (`Sun, 08 November 2026 00:00:00 GMT`), as supplied by the product owner; leave out runs not yet announced and remove the now unused `nextEventDate`/`nextEventDateUtc` keys only if the skill says the raw files may drop them (keep `eventStage`, it is the game's own field). Verify: the catalog loads (`GameCatalogLoader.Load()` in a test) and a data test asserts, for each event, the exact served array and that it is ascending with 7-day spacing.
- [x] 2.2 Update the manifest snapshot test and the release `Version` tag for the changed `lres` hash, leaving `SchemaVersion` at 5 (content-only change per the skill). Verify: the manifest snapshot test passes and the served `lres` payload for `votanUthar` shows the expected array.
- [x] 2.3 Document `runStartDatesUtc` (meaning of the index, the prefix rule, the 7-day spacing, the V1 `lre-event-dates.ts` mapping and how to add the next run) in `.agents/skills/game-catalog-data/SKILL.md` and mirror the file in `.claude/skills/game-catalog-data/SKILL.md` if the repo keeps both. Verify: the skill describes the field and both copies are identical.

## 3. Integration

- [x] 3.1 Run the full test suite and build. Verify: `dotnet build` and `dotnet test` succeed with no new warnings from the touched files.
- [ ] 3.2 Request `/api/v1/game-catalog/manifest` and the `lres` dataset from the local Aspire stack and check `votanUthar`, `tauFarsight`, `astarLysander` and `lostaBile` carry the agreed arrays. Verify: the arrays are Uthar `["2026-03-08T00:00:00Z", "2026-10-04T00:00:00Z"]`, Farsight `["2025-12-28T00:00:00Z", "2026-06-21T00:00:00Z"]`, Lysander `["2026-08-30T00:00:00Z"]` and Fabius Bile `["2026-11-08T00:00:00Z"]`.

## Workflow follow-up

- Deploy this half before the apps half; archive both halves together after the apps half is verified on staging.
