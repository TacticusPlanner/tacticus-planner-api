## Context

Source for the new occurrences: the in-game calendar image "Update 1.42" (Sun 2026-09-06 to Sat 2026-10-10). The image is day-granular, so times of day are a convention (below). Bars were re-read column by column (Sun..Sat) against the row headers.

Authored occurrences (end exclusive; convention: start 08:00 UTC on the first bar day, end 08:00 UTC on the day after the last bar day):

| Event | Definition id | startUtc | endUtc (exclusive) | Calendar bar (inclusive days) |
|---|---|---|---|---|
| Against the Tide | `hse-against-the-tide` | 2026-09-12T08:00:00Z | 2026-09-19T08:00:00Z | Sat 09-12 to Fri 09-18 |
| Purge Order | `hse-purge-order` | 2026-09-22T08:00:00Z | 2026-09-26T08:00:00Z | Tue 09-22 to Fri 09-25 |
| Squig Smash (Reworked) | `hse-squig-smash` | 2026-09-28T08:00:00Z | 2026-10-01T08:00:00Z | Mon 09-28 to Wed 09-30 |
| Machine Hunt | `hse-machine-hunt` | 2026-10-02T08:00:00Z | 2026-10-06T08:00:00Z | Fri 10-02 to Mon 10-05 |
| Training Rush | `hse-training-rush` | 2026-10-06T08:00:00Z | 2026-10-10T08:00:00Z | Tue 10-06 to Fri 10-09 |
| Against the Tide (second run) | `hse-against-the-tide` | 2026-10-10T08:00:00Z | unknown, not authored | starts Sat 10-10; end is beyond the image |

Not authored here (non-HSE items in the image, outside this change unless already modelled): Survival Event (Sep 12-18, no definition), Campaign Event 18 (a `campaign-event` definition exists; Sep 10-23 Update 1.42 window left to the normal calendar pass), TA Faction (Legendary) no MoW (Sep 9-12), TA Power-Ups (Legendary) MoW (Sep 22-25; `ta-power-ups` exists), Character Release Event Nubari Salamander Eradicator, Guild War Season 27 (Sep 19 to Oct 3), Incursion (Sep 28 to Oct 3; `incursion` exists), quests (Sekhetar Robot vs Adepta Sororitas, Ramus vs Deathguard), character/MoW releases (Votann Champion Kimm, Ghazghkull/The Lion, Khaine/Cawl, The Lion/Magnus), Legendary Release Event Uthar, Battle Pass 41 begin/S41 end, Always Double XP/Gold. They are not modelled for 1.42 in this change and not silently dropped; a later calendar pass can add them.

Investigation findings (2026-10-01):

- `event-occurrences.json` holds 20 hand-authored records; the latest HSE ends 2026-09-06 and the latest record of any kind starts 2026-09-02, so nothing covers Update 1.42. HSE definitions are `recurrence: None`, so the 15-week projection never adds placeholders for them (by design: HSE runs are announced, not periodic). The calendar is therefore only as fresh as the last manual authoring pass, which explains the missing Machine Hunt.
- Date handling: existing V2 occurrences are all authored at 00:00 UTC (a day-granular convention that fits the old calendar-bar reading); V1's `hse-schedule.ts` records 08:00:00Z starts for its two entries (Machine Hunt 2026-08-23T08:00Z to 08-27T00:00Z; Terminator boost 2026-09-01T08:00Z to 09-05T08:00Z). V2's Machine Hunt (2026-08-16 to 08-23) and Terminator Boost (2026-08-30 to 09-06) differ from V1, so at least one source is wrong. Verify against the game before authoring.
- V2 HSE definitions: faction-boost, faction-focus, warp-surge, training-rush, global-conflict-operations, arsenal-of-war, machine-hunt, terminator-boost. Missing for the 1.42 calendar: Against the Tide (not in V1 tracker data, only a text mention) and Squig Smash (Reworked) and Purge Order. V1 additionally has kill_tyranids (Purge Order), squig_smash, trait_boost_rapid_assault/flying/psyker, for_the_dark_gods, for_the_emperor, defeat_waves(_2), 11th-edition weeks 1-3 (plus global variants of the same weeks).
- Existing data overlaps: on 2026-08-09 Warp Surge, Arsenal of War and Global Conflict Operations all run. Settled: that is stale data. Arsenal of War and Global Conflict Operations were one-time events with no raid points, not repeating HSEs competing with the rotating raid-point HSEs. Decision: leave those historical records unchanged (minimal; they are in the past and harmless), note here that they are non-raid-point one-time events, and add no API overlap validation. The apps change keeps a defensive client-side pick that prefers a rule-bearing event over a non-rule one.

## Goals / Non-Goals

**Goals:** a definition for every HSE type including Against the Tide; the Update 1.42 occurrences with concrete UTC instants; a CI guard against a stale calendar.

**Non-Goals:** raid-point rules or HSE trackers/rewards (client-side, apps change); recurrence for HSEs; an admin UI or external sync for the calendar; display text (ids only, per project rule).

## Decisions

1. **Definitions are plain `None` records** with `requiredParameters: []`, now 12 new ones (the earlier list plus `hse-against-the-tide`), 20 `hse-*` ids in total. Squig Smash (Reworked) uses `hse-squig-smash`; the rework is a game-side change, not a separate id. No game-mode config is added (the apps change does not need it). Names and wiki links are apps-side.
2. **Global variants of the 11th-edition weeks are not separate definitions**; they are the same run for the global tier.
3. **Authoring uses concrete UTC instants** from the table above. Time-of-day convention (assumption, the single open question): V1's schedule uses 08:00Z starts (Machine Hunt 2026-08-23T08:00Z, Terminator 2026-09-01T08:00Z and ends at 08:00Z or 00:00Z) and the game's server day boundary comment in V1 says 00:00Z; V1 Terminator is exactly 96h, matching a four-day bar. So starts are 08:00Z on the first bar day and ends are 08:00Z on the day after the last bar day (end exclusive), which keeps back-to-back events (Machine Hunt, Training Rush) contiguous. The existing V2 midnight records stay as they are except where task 2.2 reconciles them.
3a. **Second Against the Tide run (starts Sat 2026-10-10)**: V1 holds no fixed duration for it (it is not in V1's schedule or tracker data), and the first run is 7 days, which is one data point, not a rule. The end is therefore unknown and the run is not authored; inventing it would be worse than a gap. Authoring it (start 2026-10-10T08:00:00Z plus the real end) is a follow-up from the next in-game calendar, due before 2026-10-10T08:00Z when the Training Rush ends; the stale-calendar guard (decision 4) fails on that date if it is missed.
4. **Stale-calendar guard** is a unit test over the embedded occurrences using the build date, failing when no HSE occurrence ends after today (UTC). It runs in the existing catalog test project, which CI already runs, so it needs no new workflow; it passes until 2026-10-10T08:00Z with the data above.
5. **No schema bump**; only the `event-definitions` and `events-calendar` hashes change, so the manifest snapshot is promoted per the `game-catalog-data` skill checklist.

## Risks / Trade-offs

- The stale-calendar test fails on a calendar date alone, which can break unrelated PRs when the calendar lapses. Accepted: the failure is the signal, and the fix is a data edit.
- Announced start/end times can shift; the occurrence is corrected in a later release.

## Open Questions

None.

Settled, not open: time of day is 08:00 UTC. The user read the in-game timer on 2026-10-01 at 08:40 UTC ("Machine Hunt starts in 23H 20M"), which puts the start at 2026-10-02 08:00 UTC and confirms the convention. No API overlap validation (the 2026-08-09 overlap is stale one-time-event data, left in place); the second Against the Tide run is deferred until its end is known.

## Companion change

`tacticus-planner-apps` change `add-home-screen-event-tracking` (same name). Apply and release this API change first, because the apps work reads the new definitions and occurrences.
