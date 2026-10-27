# Proposal

## Why

The apps change `add-goal-season-fit` (tacticus-planner-apps) restores V1's season color coding on the Goals page as a "Season fit" view option with Battle Pass, Guild Raid and Guild War modes. The client measures goals against season windows projected from the served `event-definitions`. Battle Pass and Guild War are already `Fixed` recurrences; `guild-raid-season` is `None` with no authored occurrences, so the client cannot place a Guild Raid season and that mode stays disabled.

Guild Raid seasons run on a fixed cycle: V1's color coding used a 14-day cycle anchored at 3 Dec 2025 05:00 UTC, and the catalog already records `seasonLengthDays: 13` (13 raid days plus the 1-day break). Severyn decided (27 Oct 2026, project thread) to encode that cycle in the catalog instead of hard-coding it in the client, which reverses the `guild-raid-current-status` rule "Guild Raid seasons SHALL NOT be projected from a guessed recurrence".

## What Changes

- `guild-raid-season` in `Data/curated/event-definitions.json` becomes `{ kind: "Fixed", intervalDays: 14, durationDays: 13, anchorUtc: "2026-10-21T05:00:00Z" }` (V1's anchor advanced by 23 cycles to the season running at authoring time). The calendar starts serving projected Guild Raid season placeholders like any other `Fixed` definition; an authored occurrence still supersedes its slot.
- `guild-raid-current-status`: the season-timing requirement no longer forbids projection. `endsAt` keeps its current source (an authored occurrence matching the observed season); projected placeholders carry no `season` parameter and so never match. Deriving `endsAt` from the projection is left out of scope.
- A catalog test pins the new recurrence and its 14-day projection; the public manifest snapshot hash for `event-definitions` changes.

## Capabilities

### New Capabilities

- none

### Modified Capabilities

- `guild-raid-current-status`: season timing may come from a `Fixed` recurrence in the catalog; `endsAt` still only from a matching authored occurrence.

## Impact

- `src/TacticusPlanner.GameCatalog/Data/curated/event-definitions.json`, `tests/TacticusPlanner.GameCatalog.Tests/EventsDenormalizerTests.cs`, `tests/TacticusPlanner.Api.Tests/GameCatalogManifestMatchesSnapshot.verified.txt`.
- No schema, migration or endpoint change. Clients pick the change up through the normal catalog sync; the home events calendar starts showing Guild Raid seasons.
- Companion apps change: `add-goal-season-fit` in `tacticus-planner-apps` (Season fit on the Goals page). This API half applies first; the apps half already disables Guild Raid mode until these dates exist, so either order is safe.
- Risk: if the real cadence drifts, one data edit here corrects every client.
