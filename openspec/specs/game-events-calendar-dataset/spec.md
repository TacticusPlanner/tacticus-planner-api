## Purpose

Provides the backend game catalog's authored and denormalized event data — reusable event definitions, scheduled occurrences, and a date-indexed calendar — so downstream clients can determine what's active or upcoming without a manual per-event authoring step for predictable recurring events.

## Requirements

### Requirement: Event definitions and occurrences carry no display text or icon
Consistent with every other served catalog dataset, `event-definitions` records (served) and `event-occurrences` records (authored, raw-only — see the served/not-served requirement below) SHALL NOT include a display name, icon, icon id, or wiki link. Only structural/identity fields exist at either layer.

#### Scenario: Definition payload shape
- **WHEN** an `event-definitions` record is served
- **THEN** it contains only structural/identity fields (id, type, rules, recurrence, references, parameters) and no display-text or icon field

#### Scenario: Authored occurrence shape
- **WHEN** an `event-occurrences` record is authored
- **THEN** it contains only structural/identity fields and no display-text or icon field, even though it is never served directly

### Requirement: Event occurrences declare explicit UTC start and end
Every `event-occurrences` record SHALL carry an explicit `startUtc` and an explicit `endUtc`. No occurrence's end SHALL be left to be inferred.

#### Scenario: Occurrence has both boundaries
- **WHEN** an occurrence is authored or projected
- **THEN** it has both an explicit `startUtc` and an explicit `endUtc`

### Requirement: Occurrence time windows and recurrence kinds are validated
Every `event-occurrences` record's `startUtc` SHALL be strictly before its `endUtc`. Every `event-definitions` record's recurrence `kind` SHALL be either `Fixed` or `None` — no other value is valid.

#### Scenario: Non-positive or inverted occurrence window fails the build
- **WHEN** an occurrence's `startUtc` is at or after its `endUtc`
- **THEN** the catalog build fails validation

#### Scenario: Unrecognized recurrence kind fails the build
- **WHEN** a definition's recurrence `kind` is neither `Fixed` nor `None`
- **THEN** the catalog build fails validation, rather than the definition being silently treated as non-recurring

### Requirement: Every occurrence's definition reference is validated
The catalog build SHALL fail if any `event-occurrences` record's `definitionId` does not resolve to an existing `event-definitions` record.

#### Scenario: Unresolvable definition reference fails the build
- **WHEN** an occurrence references a `definitionId` with no matching entry in `event-definitions`
- **THEN** the catalog build fails validation

### Requirement: Required parameters are enforced per definition
When an `event-definitions` record declares required parameters, the catalog build SHALL fail if any occurrence referencing that definition omits a value for one of them.

#### Scenario: Missing a declared required parameter fails the build
- **WHEN** an occurrence references a definition that declares a required parameter
- **AND** the occurrence's `parameters` omits a value for that parameter
- **THEN** the catalog build fails validation

#### Scenario: All required parameters present passes validation
- **WHEN** an occurrence supplies a value for every parameter its definition requires
- **THEN** the catalog build does not fail validation for that occurrence

### Requirement: Fixed-recurrence definitions are projected into a rolling 15-week window
A definition whose recurrence is `Fixed` SHALL declare a positive `intervalDays`, a positive `durationDays` (strictly less than `intervalDays`), and an `anchorUtc` reference date — every projected slot is phase-locked to `anchorUtc`, not to an arbitrary fixed point, so a weekly definition lands on its intended weekday. The definition SHALL be projected, at each catalog build, into placeholder occurrences filling every slot from the build time through 15 weeks ahead.

#### Scenario: Placeholder generated for an unscheduled future slot
- **WHEN** a `Fixed`-recurrence definition's next slot falls within 15 weeks of the catalog build time
- **AND** no occurrence has been authored for that slot
- **THEN** the served `events-calendar` includes a placeholder entry for that slot's date range

#### Scenario: No placeholder beyond the window
- **WHEN** a `Fixed`-recurrence definition's slot falls more than 15 weeks ahead of the catalog build time
- **THEN** no placeholder entry is generated for that slot

#### Scenario: Missing or non-positive recurrence fields fail the build
- **WHEN** a `Fixed`-recurrence definition is missing `intervalDays`, `durationDays`, or `anchorUtc`, or has a non-positive `intervalDays`/`durationDays`, or has `durationDays >= intervalDays`
- **THEN** the catalog build fails validation and no placeholder is projected for that definition

### Requirement: An authored occurrence supersedes its overlapping projected placeholder
When an authored `event-occurrences` record's window overlaps a projected placeholder's window for the same definition, the served `events-calendar` SHALL include only the authored occurrence for that overlap, not both. Overlap (not an exact date-range match) is what triggers supersession, so an authored occurrence whose actual dates drift slightly from the raw-cadence placeholder it replaces still supersedes it correctly.

#### Scenario: Authored occurrence replaces an overlapping placeholder
- **WHEN** an occurrence is authored whose window overlaps a previously projected placeholder's window for the same definition
- **THEN** the served `events-calendar` shows the authored occurrence for that window and no separate placeholder entry covering the same dates

### Requirement: None-recurrence definitions are never projected
A definition whose recurrence is `None` SHALL never produce a projected placeholder, at any distance from the catalog build time.

#### Scenario: No placeholder for a None-recurrence definition
- **WHEN** a `None`-recurrence definition has no authored occurrence
- **THEN** no entry for that definition appears anywhere in the served `events-calendar`

### Requirement: Served calendar is date-indexed with multi-day occurrences spanning every date
The served `events-calendar` dataset SHALL be indexed by calendar date. An occurrence or placeholder whose window spans more than one date SHALL appear as a self-contained entry under every date it spans, sharing the same occurrence identity across those dates.

#### Scenario: Single-day entry
- **WHEN** an occurrence's window falls entirely within one calendar date
- **THEN** it appears under that one date in `events-calendar`

#### Scenario: Multi-day entry spans every date
- **WHEN** an occurrence's window spans multiple calendar dates
- **THEN** it appears as an entry under every date it spans, all referencing the same occurrence identity

### Requirement: event-definitions and events-calendar are served; event-occurrences is not
`event-definitions` and `events-calendar` SHALL each be hashed into the catalog manifest and served at their own endpoint, consistent with every other served dataset. `event-occurrences` SHALL remain a raw, authored input consumed only during denormalization — it SHALL NOT be exposed as its own served endpoint.

#### Scenario: Served datasets appear in the manifest
- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `event-definitions` and for `events-calendar`

#### Scenario: Served dataset is served without authentication
- **WHEN** a client requests the `event-definitions` or `events-calendar` endpoint
- **THEN** the data is returned without requiring authentication, consistent with the rest of the game catalog

#### Scenario: event-occurrences has no direct endpoint
- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving raw `event-occurrences` records directly

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
