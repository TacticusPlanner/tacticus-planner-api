## MODIFIED Requirements

### Requirement: Currently announced Home Screen Event runs are authored
`event-occurrences` SHALL contain an occurrence, with explicit UTC start and end instants, for every HSE run that is live or announced with a known end at the time the catalog is released. Instants follow the documented convention: HSEs switch at 01:00 Europe/Prague local time, so a run starts at 01:00 Europe/Prague on its first day and ends (exclusive) at 01:00 Europe/Prague on the day it closes, converted to UTC (23:00 UTC on the previous day while Central European Summer Time applies, 00:00 UTC while Central European Time applies). A run whose end is not yet known SHALL NOT be authored with an invented end.

#### Scenario: Upcoming run is in the calendar
- **GIVEN** a Machine Hunt shown in game from 2026-10-02 01:00 to 2026-10-06 01:00 Central European Summer Time
- **WHEN** `events-calendar` is served at 2026-10-01
- **THEN** it contains an `hse-machine-hunt` entry from 2026-10-01T23:00:00Z to 2026-10-05T23:00:00Z

#### Scenario: Run that ended is not live
- **WHEN** the time is 2026-10-06T06:59:00Z
- **THEN** the `hse-machine-hunt` run that ended at 2026-10-05T23:00:00Z does not cover that instant

#### Scenario: Runs after the switch to winter time
- **WHEN** `events-calendar` is served
- **THEN** `hse-faction-focus` and `hse-global-operation-imperator` run from 2026-10-26T00:00:00Z to 2026-11-01T00:00:00Z, and `hse-warp-surge` from 2026-11-05T00:00:00Z to 2026-11-08T00:00:00Z

#### Scenario: Update 1.42 and 1.43 runs are present
- **WHEN** `events-calendar` is served
- **THEN** it contains `hse-against-the-tide` (2026-09-11T23:00:00Z to 2026-09-18T23:00:00Z), `hse-purge-order` (2026-09-21T23:00:00Z to 2026-09-25T23:00:00Z), `hse-squig-smash` (2026-09-27T23:00:00Z to 2026-09-30T23:00:00Z), `hse-training-rush` (2026-10-05T23:00:00Z to 2026-10-09T23:00:00Z), `hse-against-the-tide` (2026-10-09T23:00:00Z to 2026-10-15T23:00:00Z) and `hse-trait-boost-psyker` (2026-10-18T23:00:00Z to 2026-10-21T23:00:00Z)

#### Scenario: End not yet known
- **GIVEN** an HSE is seen starting with no announced end
- **THEN** no occurrence is authored for that run until the end is known
