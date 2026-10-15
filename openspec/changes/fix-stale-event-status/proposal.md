## Why

UserJot "Incorrect Event Times and Stale HSE Status": "The calendar is not set up right for the start and end time of events. Machine Hunt finished almost 8h ago but is showing as still active in HSE tab/page. There should be no HSE right now (`2026-10-06T06:59+00:00`)."

Every Home Screen Event authored since Update 1.42 follows an assumed "08:00 UTC" convention, and earlier ones sit at 00:00 UTC. Neither matches the game. The Tacticus Codex event calendar, read from Central Europe, shows HSE boundaries at 1:00 AM local time (for example "HSE - Against the Tide. Oct 10 1:00 AM - Oct 16 1:00 AM", "HSE - Psyker Boost. Oct 19 1:00 AM - Oct 22 1:00 AM", "HSE - Faction Focus Oct 26 1:00 AM"), and the report puts the Machine Hunt end at about 23:00 UTC on 2026-10-05. Both agree on HSEs switching at 01:00 Europe/Prague time (Snowprint's local time): 23:00 UTC the previous day while summer time applies, 00:00 UTC from 2026-10-25 on. Against the Tide (October) and Psyker Boost were also authored one day too long.

Companion apps change: `fix-stale-event-status` in `tacticus-planner-apps`, which fixes the client so an ended HSE stops showing as active without a reload and an open tab re-checks the catalog.

## What Changes

- Re-time every authored HSE occurrence to 01:00 Europe/Prague on its listed start and end dates, converted to UTC.
- Correct the end of Against the Tide (2026-10-09T23:00Z to 2026-10-15T23:00Z, not 2026-10-17) and Psyker Boost (2026-10-18T23:00Z to 2026-10-21T23:00Z, not 2026-10-23).
- Document the convention in the dataset spec and update the calendar test's expected instants.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `game-events-calendar-dataset`: HSE instants follow the 01:00 Europe/Prague convention.

## Impact

`src/TacticusPlanner.GameCatalog/Data/events/event-occurrences.json` (HSE records only; other event types unchanged) and `tests/TacticusPlanner.GameCatalog.Tests/HomeScreenEventCalendarTests.cs`. The events-calendar dataset hash changes, so clients download it again. No schema or endpoint change.

Unverified: the convention is inferred from the Codex calendar and one player report, not from game data. Pre-1.42 runs (July to early September) are re-timed by the same rule without a direct source. Training Rush on 2026-11-12 keeps its authored two-day length.
