## MODIFIED Requirements

### Requirement: Fixed-recurrence definitions are projected into a rolling 15-week window
A definition whose recurrence is `Fixed` SHALL declare a positive `intervalDays`, a positive `durationDays` (strictly less than `intervalDays`), and an `anchorUtc` reference date — every projected slot is phase-locked to `anchorUtc`, not to an arbitrary fixed point, so a weekly definition lands on its intended weekday. The definition SHALL be projected into placeholder occurrences filling every slot from the start of the current UTC day (the calendar day) through 15 weeks ahead. The projection SHALL depend only on the calendar day, never the exact load instant, so every API replica serves the same `events-calendar` — and the same content-addressed dataset URLs — on a given day; the `events-calendar` dataset, its hash and the manifest `sourceHash` SHALL be re-projected when the UTC day changes, and every other served dataset SHALL stay as compiled.

#### Scenario: Replicas loaded on the same day agree
- **WHEN** two API instances load the catalog at different times on the same UTC day
- **THEN** both serve identical dataset envelope hashes, so a dataset URL from either instance's manifest resolves on the other

#### Scenario: Placeholder generated for an unscheduled future slot
- **WHEN** a `Fixed`-recurrence definition's next slot falls within 15 weeks of the calendar day
- **AND** no occurrence has been authored for that slot
- **THEN** the served `events-calendar` includes a placeholder entry for that slot's date range

#### Scenario: No placeholder beyond the window
- **WHEN** a `Fixed`-recurrence definition's slot falls more than 15 weeks ahead of the calendar day
- **THEN** no placeholder entry is generated for that slot

#### Scenario: Missing or non-positive recurrence fields fail the build
- **WHEN** a `Fixed`-recurrence definition is missing `intervalDays`, `durationDays`, or `anchorUtc`, or has a non-positive `intervalDays`/`durationDays`, or has `durationDays >= intervalDays`
- **THEN** the catalog build fails validation and no placeholder is projected for that definition

#### Scenario: The day changes
- **WHEN** the UTC day changes while an API instance runs
- **THEN** its next snapshot re-projects `events-calendar` from the new calendar day and keeps every other served dataset's hash
