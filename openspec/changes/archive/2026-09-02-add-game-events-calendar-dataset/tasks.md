## 1. Raw data authoring

- [x] 1.1 Add `Data/events/event-definitions.json` with: `campaign-event` (Fixed, intervalDays=35, durationDays=14), `incursion` (Fixed, 35/5), `legendary-event` (Fixed, 35/7), `always-double-xp-sunday` and `always-double-gold-saturday` (Fixed, 7/1, type StandingModifier), `new-character-event` (None), `game-version-release` (None), current `hse-*` definitions (None) — including `hse-faction-boost` (`requiredParameters: ["targetFactionId"]`, Arena stat bonus) and `hse-faction-focus` (`requiredParameters: []`, no stat bonus, more game modes) as two distinct definitions — and every current Tournament Arena ruleset (`ta-faction-war`, `ta-power-ups`, `ta-conquest`, `ta-draft-power-ups`, `ta-infested-power-ups`, all None). No display name or icon field on any record.
- [x] 1.2 Add `Data/events/event-occurrences.json` seeded with currently-known real occurrences (author from whatever's actually announced/scheduled at implementation time), each supplying every `requiredParameters` value its definition declares.

## 2. Registry & models

- [x] 2.1 Add raw source keys `event-definitions`, `event-occurrences` and served keys `event-definitions`, `events-calendar` to `Models/GameCatalogDatasets.cs`.
- [x] 2.2 Add served-view records: `EventDefinition` (id, type, recurrence union `Fixed{intervalDays,durationDays}`/`None`, mechanic-specific config, `requiredParameters`) and `EventsCalendarEntry` (occurrenceId nullable, definitionId, confirmed, startUtc, endUtc, parameters), plus the `events-calendar` envelope shape (date-keyed map of `EventsCalendarEntry[]`).
- [x] 2.3 Add the raw model for `event-occurrences` (id, definitionId, startUtc, endUtc, parameters) — internal to denormalization, not a served view.

## 3. Denormalization

- [x] 3.1 Implement `Fixed`-kind recurrence projection: for each `event-definitions` record with `Fixed` recurrence, generate placeholder occurrences filling every slot from `DateTime.UtcNow` (at build time) through 15 weeks ahead, using its `intervalDays`/`durationDays`.
- [x] 3.2 Implement reconciliation: an authored `event-occurrences` record whose window overlaps a projected placeholder's window for the same `definitionId` supersedes that placeholder (per design.md Decision 3 — overlap-based, not exact-match).
- [x] 3.3 Implement the `events-calendar` date-index expansion: from the merged (authored + projected, post-reconciliation) occurrence set, build a date-keyed map; an entry spanning multiple dates is repeated under every date it spans, sharing `occurrenceId` (null for placeholders).
- [x] 3.4 Wire `event-definitions` straight through as a served dataset (no denormalization needed beyond the raw→view mapping).

## 4. Validation

- [x] 4.1 Add a cross-reference check: every `event-occurrences.definitionId` must resolve to an `event-definitions` entry; throw at load otherwise.
- [x] 4.2 Add a required-parameter check: every `event-occurrences` record must supply a value for each key in its definition's `requiredParameters`; throw at load otherwise.
- [x] 4.3 Extend `ManifestValidation` (or equivalent) to require `event-definitions` and `events-calendar` both non-empty, consistent with the existing non-empty-dataset check for other served datasets.

## 5. Hashing & manifest

- [x] 5.1 Register `event-definitions` and `events-calendar` in `Utils/GameCatalogHashing.cs` for per-dataset hashing and inclusion in `SourceHash`.

## 6. Endpoints

- [x] 6.1 Add `AllowAnonymous` served endpoints for `event-definitions` and `events-calendar` via the existing `ServedDatasetEndpoint<T>` pattern in `Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs`. Do not add an endpoint for raw `event-occurrences`.

## 7. Tests & verification

- [x] 7.1 Unit test projection: a slot exactly at the 15-week boundary and one just past it (no placeholder), a `None`-kind definition never producing a placeholder, `always-double-xp-sunday`/`always-double-gold-saturday` appearing on every Sunday/Saturday within the window.
- [x] 7.2 Unit test reconciliation: an authored occurrence overlapping a placeholder's window supersedes it; an authored occurrence with no corresponding placeholder appears normally; two placeholders for the same definition never overlap each other (per design.md Risk 1) — enforced via a load-time validation error, tested directly.
- [x] 7.3 Unit test the `events-calendar` date-index expansion: single-day entry, multi-day entry appearing under every spanned date with the same `occurrenceId`.
- [x] 7.4 Unit test validation failures: unresolvable `definitionId`, missing required parameter (using the Faction Boost/Faction Focus pair as the concrete case) — and the corresponding success cases.
- [x] 7.5 `dotnet build TacticusPlanner.slnx` to confirm startup load + validation passes with the new seeded data.
- [x] 7.6 `dotnet test TacticusPlanner.slnx`; review and promote the Verify manifest snapshot diff (`GameCatalogSnapshotTests.GameCatalogManifestMatchesSnapshot.received.txt` → `.verified.txt`) to include the two new dataset hashes. All tests pass except `OnslaughtProgressEndpointTests.PutPreservesOtherPlayerDataOverrides`, confirmed pre-existing (fails identically in complete isolation, unrelated domain — not caused by this change).
