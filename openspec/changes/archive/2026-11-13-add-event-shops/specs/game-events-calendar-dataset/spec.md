## MODIFIED Requirements

### Requirement: Required parameters are enforced per definition
When an `event-definitions` record declares required parameters, the catalog build SHALL fail if any occurrence referencing that definition omits a value for one of them. An occurrence of a `HomeScreenEvent` definition MAY also carry the optional parameter `hseVariant`; when present it SHALL name a variant of that definition's `config.hse` (see `home-screen-event-scoring`), or the catalog build SHALL fail. An occurrence of a `ShopEvent` definition MAY also carry the optional parameters `incomeFree` and `incomeBonusShipment`; when present each SHALL be a non-negative integer (estimated event currency for that week), or the catalog build SHALL fail.

#### Scenario: Missing a declared required parameter fails the build
- **WHEN** an occurrence references a definition that declares a required parameter
- **AND** the occurrence's `parameters` omits a value for that parameter
- **THEN** the catalog build fails validation

#### Scenario: All required parameters present passes validation
- **WHEN** an occurrence supplies a value for every parameter its definition requires
- **THEN** the catalog build does not fail validation for that occurrence

#### Scenario: Known HSE variant passes validation
- **WHEN** an `hse-faction-focus` occurrence carries `hseVariant` `october-2026` and the definition has that variant
- **THEN** the catalog build does not fail validation and the occurrence is served with the parameter

#### Scenario: Unknown HSE variant fails the build
- **WHEN** an `hse-defeat-waves` occurrence carries `hseVariant` `run-3` and the definition has no such variant
- **THEN** the catalog build fails validation

#### Scenario: Shop event income passes validation
- **WHEN** an `anniversary-event` occurrence carries `incomeFree` 750 and `incomeBonusShipment` 1000
- **THEN** the catalog build does not fail validation and the occurrence is served with both parameters

#### Scenario: Negative shop event income fails the build
- **WHEN** a `ShopEvent` occurrence carries `incomeFree` -1
- **THEN** the catalog build fails validation

## ADDED Requirements

### Requirement: Shop event occurrences name their event shop

Every `ShopEvent` definition SHALL declare the required parameters `weekNumber` and `eventKey`. `eventKey` SHALL be the datamine event key `<Month><Year>` (e.g. `October2026`) whose event-shop merchants the week belongs to; the `event-shops` dataset (see `game-event-shops-dataset`) dates events from these occurrences, and a CI test fails when a not-yet-ended occurrence's `eventKey` has no event shop. `eventKey` SHALL match `^[A-Z][a-z]+[0-9]{4}$` and `weekNumber` SHALL be a positive integer, or the catalog build SHALL fail. Each seasonal event shop SHALL have one occurrence per week. The calendar SHALL carry a `ShopEvent` definition `armageddon-shop` (recurrence `None`) alongside `anniversary-event`, and occurrences for the event shops currently in the pinned datamine: `armageddon-shop` weeks 1–3 of `June2026` from 2026-06-22, `anniversary-event` weeks 1–2 of `August2026` from 2026-08-02, and `armageddon-shop` week 1 of `October2026` from 2026-10-26.

#### Scenario: Shop event occurrence without eventKey fails the build

- **WHEN** an `anniversary-event` occurrence has `weekNumber` 1 and no `eventKey`
- **THEN** the catalog build fails validation

#### Scenario: Armageddon weeks appear on the calendar

- **WHEN** the events calendar is served for June 2026
- **THEN** it has three `armageddon-shop` entries starting 2026-06-22, 2026-06-29 and 2026-07-06, each lasting 7 days
