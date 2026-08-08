## Purpose

Provides the backend game catalog's authored and denormalized event data — reusable event definitions, scheduled occurrences, and a date-indexed calendar — so downstream clients can determine what's active or upcoming without a manual per-event authoring step for predictable recurring events.

## ADDED Requirements

### Requirement: Event definitions and occurrences carry no display text or icon
Consistent with every other served catalog dataset, `event-definitions` and `event-occurrences` records SHALL NOT include a display name, icon, icon id, or wiki link. Only structural/identity fields are served.

#### Scenario: Definition and occurrence payload shape
- **WHEN** an `event-definitions` or `event-occurrences` record is served
- **THEN** it contains only structural/identity fields (id, type, rules, recurrence, references, parameters) and no display-text or icon field

### Requirement: Event occurrences declare explicit UTC start and end
Every `event-occurrences` record SHALL carry an explicit `startUtc` and an explicit `endUtc`. No occurrence's end SHALL be left to be inferred.

#### Scenario: Occurrence has both boundaries
- **WHEN** an occurrence is authored or projected
- **THEN** it has both an explicit `startUtc` and an explicit `endUtc`

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
A definition whose recurrence is `Fixed` (an interval and a duration) SHALL be projected, at each catalog build, into placeholder occurrences filling every slot from the build time through 15 weeks ahead.

#### Scenario: Placeholder generated for an unscheduled future slot
- **WHEN** a `Fixed`-recurrence definition's next slot falls within 15 weeks of the catalog build time
- **AND** no occurrence has been authored for that slot
- **THEN** the served `events-calendar` includes a placeholder entry for that slot's date range

#### Scenario: No placeholder beyond the window
- **WHEN** a `Fixed`-recurrence definition's slot falls more than 15 weeks ahead of the catalog build time
- **THEN** no placeholder entry is generated for that slot

### Requirement: An authored occurrence supersedes its projected placeholder
When an authored `event-occurrences` record covers the same definition and date window as a projected placeholder, the served `events-calendar` SHALL include only the authored occurrence for that window, not both.

#### Scenario: Authored occurrence replaces the placeholder
- **WHEN** an occurrence is authored for the same definition and date range as a previously projected placeholder
- **THEN** the served `events-calendar` shows the authored occurrence for that range and no separate placeholder entry

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
