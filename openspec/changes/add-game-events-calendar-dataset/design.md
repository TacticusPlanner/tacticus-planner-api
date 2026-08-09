## Context

See `proposal.md` for motivation. Relevant current state (see this repo's `game-catalog-data` skill for the full pipeline):

- Raw sources live under `Data/**`, discovered by convention (dataset key `foo-bar` → embedded `foo-bar.json`, matched by leaf filename). `Models/GameCatalogDatasets.cs` is the registry of raw source keys and served keys.
- `Denormalization/*.cs` is a `partial class GameCatalogDenormalizer`, one file per entity, building served views from raw collections. `Validation/*.cs` is a `partial class GameCatalogValidator` running over the **raw** snapshot at load, throwing on any error.
- Every served dataset today carries only structural/identity fields — no `icon`/`iconId`/`wikiLink` anywhere in the catalog. This change's definitions/occurrences follow the same rule.
- `GameCatalogRelease.SchemaVersion` is bumped only on a breaking shape change to an existing served dataset; adding new dataset keys is not itself a breaking change to any existing schema.
- The client-side counterpart (`integrate-game-events-calendar` in `tacticus-planner-apps`) only ever consumes `eventDefinitions` and `eventsCalendar` — it never needs raw `eventOccurrences`, which drives the decision below to keep occurrences unserved.

## Goals / Non-Goals

**Goals:**
- Land the three new dataset keys (`event-definitions` served, `event-occurrences` raw-only, `events-calendar` served) inside the existing pipeline shape with no new architectural pattern.
- Make the `Fixed`/`None` recurrence projection and placeholder/authored reconciliation a single, testable denormalization unit.
- Make the Faction Boost vs. Faction Focus style distinction a build-time validation failure, not a convention.

**Non-Goals:**
- Any client-side work (IndexedDB, selectors, UI) — tracked in `tacticus-planner-apps`'s `integrate-game-events-calendar`.
- An authoring UI for `event-occurrences` — stays a hand-edited JSON file, same as every other catalog source.
- Versioning event *definitions* by game version — only a `game-version-release` marker dataset entry, no rules-versioning.

## Decisions

**1. `event-definitions` is served as-is; `event-occurrences` is raw-only, feeding `events-calendar`.**
The client needs a definition's actual rules (scoring, applicable game modes, required parameters) for anything beyond rendering — e.g. a future feature reasoning about which game modes an active HSE applies to. It does not need raw, unmerged occurrences; every occurrence-level fact it needs (which definition, confirmed vs. placeholder, the resolved parameters, the date range) is already flattened into `events-calendar`. Serving occurrences directly would just duplicate `events-calendar`'s content in an unmerged, harder-to-consume shape.
- _Alternative considered_: serve all three raw/denormalized datasets uniformly. Rejected — `event-occurrences` alone, without the projection/reconciliation step applied, isn't useful to any known client consumer, and not serving it keeps one fewer dataset key to hash/version/maintain.

**2. Recurrence projection and the date-index expansion live together in one new `Denormalization` unit, not split across two.**
Projection (turning a `Fixed`-recurrence definition into placeholder occurrences) and date-expansion (turning the merged authored+placeholder occurrence set into date-keyed `events-calendar` entries) are sequential steps of the same computation — projection must run before expansion can see the full occurrence set for a given definition. Keeping them in one unit avoids a partially-built intermediate model leaking into another file's public surface.

**3. The reconciliation rule is "any authored occurrence whose window overlaps a placeholder's window, for the same `definitionId`, replaces that placeholder" — not an exact date-range match.**
An exact-match requirement would silently leave a stale placeholder on the calendar if an authored occurrence's actual dates shift slightly from what a placeholder assumed (e.g. a Campaign Event running a few days later than the raw 35-day cadence would predict). Overlap-based supersession is more robust to that kind of drift and matches the intent (there is one real event happening in that neighborhood, so only one entry should show).

**4. `Fixed` recurrence projection window is computed relative to the catalog build time (`DateTime.UtcNow` at denormalization), not a stored/configurable value.**
Consistent with the 15-week rule decided in the client-side design — this is a pure function of "now," recomputed fresh every time the catalog is built/deployed, with no persisted projection state to go stale or drift from reality.

**5. Required-parameter validation is a new `Validation/*.cs` cross-reference check, structurally identical to existing cross-reference checks (e.g. `campaign-definitions`/`lres` `battleIds` resolving to a served battle).**
`event-definitions.requiredParameters` (a list of parameter keys) is checked against each referencing `event-occurrences` record's `parameters` map; any missing key throws at load, same failure mode as every other validation error in this pipeline (fail fast, no partial catalog).

## Risks / Trade-offs

- [Risk] Overlap-based reconciliation (Decision 3) could, in a pathological authoring mistake, match an authored occurrence against the wrong placeholder if two placeholders for the same definition were ever adjacent enough to overlap ambiguously. → Mitigation: enforced at load, not just assumed — validation rejects any `Fixed`-kind definition whose `durationDays >= intervalDays` (see `Validation/EventsValidation.cs`), so adjacent placeholders for the same definition provably cannot overlap each other.
- [Risk] Discovered during implementation: a `Fixed`-kind definition also requires `anchorUtc` (added after the first draft — projecting from an implicit fixed point like the Unix epoch would phase-lock every weekly modifier to whatever weekday that point happens to fall on, not the intended one). Denormalization runs before validation in this pipeline, so a definition missing `anchorUtc`/`intervalDays`/`durationDays` is skipped defensively during projection (not projected, not a crash) and only surfaces as a proper validation error afterward, rather than a raw null-dereference.
- [Trade-off] Projection recomputes from scratch on every catalog build rather than persisting projected state — cheap given the bounded 15-week/weekly-modifier volume, but means the exact set of "which future dates currently show a placeholder" is only knowable by re-running denormalization, not queryable independently. Accepted; nothing currently needs that queried in isolation.
- [Risk] `event-occurrences` not being served (Decision 1) means if a future consumer genuinely needs raw, unmerged occurrence data, this would require a follow-up change to add an endpoint. → Mitigation: no known consumer needs this today; revisit if one appears.
- [Risk] Discovered during implementation: `events-calendar`'s content depends on the load-time "now" (Decision 4), so its dataset hash — and therefore the aggregate `sourceHash` that includes every dataset hash — legitimately changes on every process start, even with no actual data change. This would make the Verify-based manifest snapshot test (`GameCatalogSnapshotTests`) permanently flaky if left unaddressed. → Mitigation: the test scrubs the `sourceHash` and the `events-calendar` dataset's hash to a fixed placeholder before comparing against the committed snapshot; every other dataset's hash remains deterministic and is still verified exactly, so the test still catches unintended drift everywhere except this one, deliberately time-variant dataset.
- [Risk] Flagged in review: `EmbeddedGameCatalogProvider.Current` is an immutable singleton set once in its constructor (`Current = GameCatalogLoader.Load()`) and never refreshed. A process that runs longer than the 15-week projection horizon without a redeploy would keep serving `events-calendar` placeholders projected from its original startup time — the horizon wouldn't slide forward with real time. → Accepted: this catalog (like every other dataset here) is versioned and re-deployed with each content update, well inside a 15-week window in practice; introducing a background-refreshed snapshot would add mutable-state/thread-safety complexity to an otherwise fully-immutable singleton for a scenario that doesn't occur under this project's actual deployment cadence. Revisit if uptime between deploys ever approaches the horizon.

## Migration Plan

Purely additive: new dataset keys, no changes to any existing dataset's shape or `SchemaVersion`. Ships as a normal catalog content release — the manifest's per-dataset hashing means existing clients unaware of the new keys are unaffected; new clients pick up `event-definitions`/`events-calendar` through the existing manifest-diff sync once the corresponding client-side change lands. Rollback is a plain revert; no data backfill in either direction.
