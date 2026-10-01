## 1. Definitions

- [x] 1.1 Add `hse-purge-order`, `hse-squig-smash`, `hse-against-the-tide`, `hse-trait-boost-rapid-assault`, `hse-trait-boost-flying`, `hse-trait-boost-psyker`, `hse-for-the-dark-gods`, `hse-for-the-emperor`, `hse-defeat-waves`, `hse-11th-edition-week-1`, `hse-11th-edition-week-2`, `hse-11th-edition-week-3` to `Data/events/event-definitions.json` (type `HomeScreenEvent`, recurrence `None`, `requiredParameters: []`, no display text). Verify: the catalog loads and `event-definitions` serves all 20 `hse-*` ids.

## 2. Occurrences

- [x] 2.1 Add these occurrences to `Data/events/event-occurrences.json` (ids like `occ-2026-09-12-hse-against-the-tide`, `parameters: null`; times follow design decision 3):

  | Event | Definition id | startUtc | endUtc (exclusive) | Calendar bar (inclusive days) |
  |---|---|---|---|---|
  | Against the Tide | `hse-against-the-tide` | 2026-09-12T08:00:00Z | 2026-09-19T08:00:00Z | Sat 09-12 to Fri 09-18 |
  | Purge Order | `hse-purge-order` | 2026-09-22T08:00:00Z | 2026-09-26T08:00:00Z | Tue 09-22 to Fri 09-25 |
  | Squig Smash (Reworked) | `hse-squig-smash` | 2026-09-28T08:00:00Z | 2026-10-01T08:00:00Z | Mon 09-28 to Wed 09-30 |
  | Machine Hunt | `hse-machine-hunt` | 2026-10-02T08:00:00Z | 2026-10-06T08:00:00Z | Fri 10-02 to Mon 10-05 |
  | Training Rush | `hse-training-rush` | 2026-10-06T08:00:00Z | 2026-10-10T08:00:00Z | Tue 10-06 to Fri 10-09 |
  | Against the Tide (second run) | `hse-against-the-tide` | 2026-10-10T08:00:00Z | unknown: NOT authored (see 2.3) | starts Sat 10-10 |

  Verify: each authored record has explicit `startUtc`/`endUtc`, the occurrence validators pass, and `events-calendar` served at 2026-10-01 contains them. If the user changes the time-of-day answer (design Open Question 1), adjust all instants before release.
- [ ] 2.2 Reconcile the existing Machine Hunt (08-16 to 08-23) and Terminator Boost (08-30 to 09-06) records with V1's schedule (Machine Hunt 2026-08-23T08:00Z to 08-27, Terminator 2026-09-01T08:00Z to 09-05T08:00Z) against the game, and correct the wrong source (apply the same 08:00Z convention if the game confirms it). The 2026-08-09 overlap (Warp Surge, Arsenal of War, Global Conflict Operations) is stale one-time-event data: leave those records as they are. Verify: the decision and evidence are noted in the commit message.
- [ ] 2.3 Do not author the second Against the Tide run (start 2026-10-10T08:00:00Z, end unknown). Verify: a follow-up item exists to add it from the next in-game calendar before 2026-10-10T08:00Z; the stale-calendar guard (3.1) fails on that date if it is missed.

## 3. Guard and tests

- [x] 3.1 Add the stale-calendar CI guard in `TacticusPlanner.GameCatalog.Tests` (runs in the existing CI test job, no new workflow): fails when no HSE occurrence ends after today (UTC), with a message naming the latest HSE end. Verify: passes now (latest end 2026-10-10T08:00:00Z); a variant using a fixed past date and the same helper fails as expected.
- [x] 3.2 Add a test asserting every definition id listed in 1.1 (including `hse-purge-order`) exists, and that the 1.42 occurrences in 2.1 exist with the exact instants. Verify: test passes.

## 4. Release

- [x] 4.1 Run the `game-catalog-data` skill checklist: promote the manifest Verify snapshot (only the `event-definitions` / `events-calendar` hashes move), `dotnet format TacticusPlanner.slnx --verify-no-changes`, full test run. Do not bump `SchemaVersion`.
- [x] 4.2 Run `openspec validate add-home-screen-event-tracking` in this repo; confirm the apps companion change of the same name is up to date.

## Deferred / out-of-session

Archived with the following tasks left unchecked (archive explicitly authorised by the user). Tracking issue: not filed yet.

- 2.2 Reconcile the Machine Hunt and Terminator Boost records with V1's schedule: needs in-game verification of the real dates (V1 vs V2 source disagree); cannot be settled from code or docs alone.
- 2.3 Second Against the Tide run (start 2026-10-10T08:00:00Z, end unknown) not authored: the end date is unknown until the next in-game calendar is available. Follow-up: add it before 2026-10-10T08:00Z; the stale-calendar guard (3.1) fails on that date if missed.
