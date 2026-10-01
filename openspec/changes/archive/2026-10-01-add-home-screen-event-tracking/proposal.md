## Why

The apps change `add-home-screen-event-tracking` makes Daily Raids event-aware by reading the active Home Screen Event (HSE) from the served events calendar. The calendar cannot support that today:

- The authored occurrences in `Data/events/event-occurrences.json` end on 2026-09-06 (the latest HSE; the latest record starts 2026-09-02), so Update 1.42 (2026-09-06 to 2026-10-10) is missing, and every `hse-*` definition has `recurrence: None`, so nothing is projected. Any HSE announced after that date (for example the Machine Hunt starting 2026-10-02) is absent from the served calendar until someone hand-authors it.
- Only 8 HSE definitions exist (Against the Tide, Purge Order and Squig Smash, all in the 1.42 calendar, are missing). V1 knows about more event types (Purge Order / Kill Tyranids, Squig Smash, the 11th-edition weeks, For the Dark Gods / For the Emperor, Rapid Assault / Flying / Psyker trait boosts, Defeat Waves). Purge Order in particular awards campaign-raid points, so the raid optimiser needs it.

## What Changes

- Add `event-definitions` records (recurrence `None`) for every HSE type V1 or the game has that the catalog lacks: `hse-purge-order`, `hse-squig-smash`, `hse-against-the-tide`, `hse-trait-boost-rapid-assault`, `hse-trait-boost-flying`, `hse-trait-boost-psyker`, `hse-for-the-dark-gods`, `hse-for-the-emperor`, `hse-defeat-waves`, `hse-11th-edition-week-1`, `hse-11th-edition-week-2`, `hse-11th-edition-week-3`.
- Author `event-occurrences` for the HSE runs in the in-game Update 1.42 calendar (Against the Tide 2026-09-12, Purge Order 2026-09-22, Squig Smash 2026-09-28, Machine Hunt 2026-10-02, Training Rush 2026-10-06; concrete UTC instants in design.md, 08:00 UTC convention). The second Against the Tide run starting 2026-10-10 is not authored because its end is unknown. Non-HSE calendar items are not authored here (listed in design.md). Re-check the existing Machine Hunt (2026-08-16 to 08-23) against V1's schedule (2026-08-23T08:00Z to 08-27) and fix whichever is wrong.
- Add a catalog test that fails when the calendar has no HSE occurrence that is live or upcoming as of the build date (stale-calendar guard), so a gap like this is caught in CI instead of by players.
- No served-shape change: `SchemaVersion` is unchanged; only the `event-definitions` and `events-calendar` dataset hashes move.
- Companion change (same name) in `tacticus-planner-apps`. **API applied first.** The apps change adds the matching `events` i18n keys for the new definition ids in every locale.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `game-events-calendar-dataset`: every known HSE type has a definition; the authored calendar must stay current.

## Impact

`Data/events/event-definitions.json`, `Data/events/event-occurrences.json`, catalog tests (`TacticusPlanner.GameCatalog.Tests`), the manifest Verify snapshot (`GameCatalogSnapshotTests`). No endpoint, model, or database change.
