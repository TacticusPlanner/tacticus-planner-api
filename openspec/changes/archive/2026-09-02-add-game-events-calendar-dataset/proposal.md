## Why

The client needs structured, queryable game-event data — event definitions, scheduled occurrences, and a date-indexed calendar — to replace a hand-maintained static calendar image and to know what's active without a manual authoring step for every predictable event slot. This is the backend half of [TacticusPlanner/tacticus-planner-apps#81](https://github.com/TacticusPlanner/tacticus-planner-apps/issues/81); the client-side consumption (IndexedDB stores, selectors, calendar UI) is tracked separately as `integrate-game-events-calendar` in the `tacticus-planner-apps` repo, which this dataset unblocks.

## What Changes

- Add two new authored raw datasets to the game catalog: `event-definitions` (reusable event mechanics — scoring, applicable game modes, required parameters, recurrence) and `event-occurrences` (scheduled instances referencing a definition, with explicit UTC start/end and typed parameters).
- Serve `event-definitions` directly (clients need its rules — scoring, applicable game modes, required parameters — not just a display id). `event-occurrences` is raw/authored input only and is **not** served directly; it's consumed entirely by a new denormalized, served dataset, `events-calendar`: date-indexed, self-contained entries, computed at catalog build time (merging authored occurrences with projected placeholders — see below), so clients never join occurrences against definitions themselves for calendar rendering.
- Add server-side projection: definitions with `Fixed` recurrence (a known interval and duration) are projected into placeholder calendar entries covering a rolling 15-week window; an authored occurrence covering the same window supersedes its placeholder. Definitions with `None` recurrence are never projected — they appear only once an occurrence is authored.
- Extend catalog validation to cross-reference every occurrence's `definitionId` and to enforce that every occurrence supplies a value for each of its definition's declared required parameters.
- Register the three new dataset keys in the manifest/hashing pipeline and serve them via new `AllowAnonymous` endpoints, following the existing pattern for the other 14 datasets.
- Event definitions and occurrences carry no display text or icon — those are resolved client-side from `id`/parameters, consistent with the served catalog's existing "structural/identity only" convention (no `icon`/`iconId`/`wikiLink` fields anywhere else in the catalog either).

## Capabilities

### New Capabilities
- `game-events-calendar-dataset`: the backend event definition/occurrence/calendar dataset — authoring shape, recurrence-based projection, and validation.

### Modified Capabilities
- none — this adds new datasets to the existing game-catalog pipeline; no existing served dataset's requirements change.

## Impact

- **Data**: new `Data/events/event-definitions.json`, `Data/events/event-occurrences.json`.
- **Models**: new raw source keys (`event-definitions`, `event-occurrences`) and new served keys (`event-definitions`, `events-calendar`) in `Models/GameCatalogDatasets.cs`; new served-view records for `event-definitions` and `events-calendar`.
- **Denormalization**: new `Denormalization/*.cs` unit implementing `Fixed`-kind projection and the date-index expansion for `events-calendar`.
- **Validation**: `Validation/*.cs` extended for `definitionId` cross-references and required-parameter enforcement.
- **Hashing/manifest**: `Utils/GameCatalogHashing.cs` registers the three new dataset keys; the Verify-guarded manifest snapshot test updates accordingly.
- **Endpoints**: `Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs` gains endpoints for the new served datasets.
- No breaking change to any of the 14 existing served datasets or their schema version.
