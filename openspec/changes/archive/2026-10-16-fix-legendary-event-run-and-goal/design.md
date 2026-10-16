# Design

## Context

The raw event files are embedded JSON (`Data/lres/lres-*.json`). Each has `eventStage` (V1's "which run the date below starts"), `nextEventDate` and `nextEventDateUtc` (one RFC 1123 date) and `finished`. The C# record `GameCatalogLre` keeps only `NextEventDateUtc` ("input to the served array; not served directly") and ignores `eventStage`. `LreDenormalizer.BuildEventStageStartDates` turns that single string into a one-element `eventStageStartDatesUtc` array, a comment there says the client derives the stage from the array. The client (`deriveLegendaryEventLifecycle`) already sorts the array and treats each element as a 7-day run window, but has no way to know the element's run number, so it reads the synced `currentEventRun`, which the game sends as `null` between runs. See proposal.md for the symptoms. Companion apps change: `fix-legendary-event-run-and-goal` in `tacticus-planner-apps` (capability `legendary-event-run-resolution`).

Raw state today (2026-10-16): Uthar `eventStage` 2, date 2026-10-04; Farsight stage 2, TBA; Lysander stage 1, TBA; Fabius Bile stage 1, date 2026-11-08. The owner supplied the real schedule: a run is one appearance of the event, lasting 7 days, with months between runs (Farsight 2025-12-28 and 2026-06-21; Uthar 2026-03-08 and 2026-10-04; Lysander 2026-08-30; Fabius Bile 2026-11-08, all Sundays). The game data also carries `eventStage` (the current run) and `heroEventMaxRuns` 3, which agree with these dates. V1's hand-kept `lre-event-dates.ts` holds only one date per event and is stale (Fabius Bile 2026-11-11, Lysander and Farsight earlier dates).

## Goals / Non-Goals

**Goals:**

- Serve each event's announced run starts by run (index = run − 1) so the client can derive current, ended and next runs.
- Reject malformed schedules at load time, like the reward ladder.
- Keep the served shape (`string[]`) so no client contract or `SchemaVersion` change is needed.

**Non-Goals:**

- A server-side current run, lifecycle or "next run" field (the client derives them; the server stays a catalog).
- Storing the last seen synced run per account, or run records changes (not needed once the schedule is in the catalog).
- Automating the dates from the game or V1 (they stay hand-curated, as `lre-event-dates.ts` is).
- Changing `finished` semantics.

## Decisions

**D1. The array index carries the run number.** Alternatives: an object list `{ run, startUtc }` (explicit, but a shape change for a field the client already reads and a `SchemaVersion` bump), or keeping one date plus serving `eventStage`. Chosen: keep `string[]` and make the index mean the run. It is the smallest change, matches the existing comment ("the client derives the current event stage from this array") and V1's three-round model. The cost is the "no gap" rule: a run cannot be announced before an earlier run is listed.

**D2. Raw field `runStartDatesUtc`, no single-date fallback.** Reading the old `nextEventDateUtc` as "the next run's date" would keep the ambiguity that causes the bug (Uthar's lone date is run 2, not run 1). The four embedded files are migrated in this change, `NextEventDateUtc` is removed from the record and the denormalizer, and `eventStage` stays ignored. Alternative: keep both and prefer the new field. Rejected: two sources for one fact.

**D3. Minimum spacing of 7 days between starts.** A run is a 7-day window in the client; closer starts would overlap and make "active" ambiguous. Gaps longer than 7 days are allowed (runs need not be contiguous).

**D4. Validation lives with the other LRE checks.** A `ValidateLreRunStarts` next to `ValidateLreRewards`, using the same error shape (`GameCatalogValidationError`, code `InvalidRunStartDates`), run from `GameCatalogValidator`. Alternative: reject in the denormalizer. Rejected: validation is a loader concern and the denormalizer assumes valid input.

**D5. Data comes from the owner.** The dates are facts only the maintainers know (V1's file is stale); the owner supplied them (task 2.1 lists the arrays). A run whose date is not announced is left out (the prefix rule), which the client reads as "not announced" (run 3 of Farsight and Uthar, run 2 of Lysander, runs 2 and 3 of Fabius Bile).

## Risks / Trade-offs

- [Wrong or missing dates keep the client unable to tell the run] → The client falls back to a number-free state (apps design D5), so a missing date degrades to vague copy rather than a wrong run number; the loader rejects structurally invalid schedules.
- [Hand-curated data goes stale as runs are announced] → Same exposure as today's single date; the `game-catalog-data` skill documents the file and its V1 source so refreshes are routine.
- [A content change to `lres` changes its dataset hash and every client re-downloads it] → Intended and cheap (the dataset is small); the manifest snapshot test is updated in the same change.
- [Removing `NextEventDateUtc` breaks any other reader] → Only the denormalizer reads it (checked: one use); the apps read the served array only.

## Migration Plan

No EF Core migration, no schema change. Order: (1) land this API half and deploy; clients keep working with the new array because the apps read the same field and treat missing dates as unknown; (2) deploy the apps half. Roll back by reverting the four data files and the denormalizer; the previous single-date behaviour returns.
