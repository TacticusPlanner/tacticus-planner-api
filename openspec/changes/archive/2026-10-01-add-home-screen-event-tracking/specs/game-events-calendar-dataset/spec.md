## ADDED Requirements

### Requirement: Every known Home Screen Event type has a definition
`event-definitions` SHALL contain a `type: HomeScreenEvent` definition for every HSE type that exists in V1's event data or in the game, including Purge Order (`hse-purge-order`), Against the Tide (`hse-against-the-tide`), Warp Surge, Machine Hunt, Training Rush, Squig Smash, Arsenal of War, Global Conflict Operations, Faction Boost, Faction Focus, the Terminator, Rapid Assault, Flying and Psyker trait boosts, For the Dark Gods, For the Emperor, Defeat Waves, and the three 11th-edition weeks. Each SHALL use `recurrence.kind` `None`.

#### Scenario: Purge Order is defined
- **WHEN** `event-definitions` is served
- **THEN** it contains `hse-purge-order` with type `HomeScreenEvent`

#### Scenario: A new event type appears in the game
- **WHEN** the game adds an HSE type that has no definition
- **THEN** the definition is added in the same change that authors its first occurrence

### Requirement: Currently announced Home Screen Event runs are authored
`event-occurrences` SHALL contain an occurrence, with explicit UTC start and end instants, for every HSE run that is live or announced with a known end at the time the catalog is released. Instants follow the documented convention (start 08:00 UTC on the first day shown in game, end exclusive at 08:00 UTC on the day after the last day shown) and SHALL NOT be rounded to midnight unless the game is verified to switch at midnight. A run whose end is not yet known SHALL NOT be authored with an invented end.

#### Scenario: Upcoming run is in the calendar
- **GIVEN** a Machine Hunt announced to start on 2026-10-02
- **WHEN** `events-calendar` is served at 2026-10-01
- **THEN** it contains an `hse-machine-hunt` entry from 2026-10-02T08:00:00Z to 2026-10-06T08:00:00Z

#### Scenario: Update 1.42 runs are present
- **WHEN** `events-calendar` is served
- **THEN** it contains `hse-against-the-tide` (2026-09-12T08:00:00Z to 2026-09-19T08:00:00Z), `hse-purge-order` (2026-09-22T08:00:00Z to 2026-09-26T08:00:00Z), `hse-squig-smash` (2026-09-28T08:00:00Z to 2026-10-01T08:00:00Z) and `hse-training-rush` (2026-10-06T08:00:00Z to 2026-10-10T08:00:00Z)

#### Scenario: End not yet known
- **GIVEN** Against the Tide is seen starting 2026-10-10 with no announced end
- **THEN** no occurrence is authored for that run until the end is known

### Requirement: Stale HSE calendar fails CI
A catalog test SHALL fail when no `type: HomeScreenEvent` occurrence has an end instant after the build date, so an unmaintained calendar is detected before release.

#### Scenario: All HSE occurrences are in the past
- **WHEN** every authored HSE occurrence ends before the current date
- **THEN** the stale-calendar test fails with a message naming the latest HSE end date
