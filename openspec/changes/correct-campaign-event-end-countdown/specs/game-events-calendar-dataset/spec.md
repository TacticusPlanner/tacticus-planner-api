## ADDED Requirements

### Requirement: Confirmed campaign-event boundaries come from verified occurrences

For a campaign-event window whose actual start and end have been verified against dated game evidence, the raw `event-occurrences` dataset SHALL carry one authored record with `id: string`, `definitionId: "campaign-event"`, `startUtc: UTC timestamp`, `endUtc: UTC timestamp`, and `parameters: object or null`. Denormalization SHALL supersede the overlapping fixed-recurrence placeholder and serve the authored window in `events-calendar`, indexed under each spanned UTC date. The served entry shape SHALL remain `occurrenceId: string or null`, `definitionId: string`, `confirmed: boolean`, `startUtc: UTC timestamp`, `endUtc: UTC timestamp`, and `parameters: object or null`; an authored entry SHALL have its ID and `confirmed: true`, while an unverified projection SHALL have `occurrenceId: null` and `confirmed: false`. The client derives names, icons, and countdowns; the server sends none of them.

The `campaign-event` fixed recurrence SHALL remain phase-locked to its verified anchor with a 35-day interval and nominal 14-day duration, projected through the existing rolling 15-week horizon. An authored occurrence SHALL supersede only an overlapping slot. The occurrence data belongs to the catalog release carrying it: a content correction SHALL change the `events-calendar` dataset hash/manifest source hash and SHALL NOT require a served schema-version bump while the shape stays unchanged.

#### Scenario: Verified end differs from placeholder

- **GIVEN** a dated source verifies a campaign-event occurrence ending later than its projected placeholder
- **WHEN** the calendar is built for a time inside that occurrence
- **THEN** the served calendar contains the authored start and end with `confirmed: true` and its non-null occurrence ID, without a second overlapping placeholder

#### Scenario: A future slot is not verified

- **WHEN** a later campaign-event slot has no authored occurrence within the 15-week horizon
- **THEN** its projected entry keeps `confirmed: false`, `occurrenceId: null`, and boundaries derived from the 35-day/14-day recurrence

#### Scenario: Corrected content reaches clients

- **WHEN** a verified occurrence changes the served event calendar without changing its shape
- **THEN** the `events-calendar` content hash changes so a client with the previous hash can refresh it, while the schema version remains unchanged
