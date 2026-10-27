## MODIFIED Requirements

### Requirement: The server sends no derived run information

The server SHALL NOT send a run number, a lifecycle state, a "next run", an "ended runs" value or a `finished` flag for an event; the client derives them from `eventStageStartDatesUtc`, `maxRuns` and the current instant (an event is finished once all `maxRuns` run starts are listed and the last run's 7 days have passed). `maxRuns` is the game's run count (`heroEventMaxRuns`), not state derived from the clock.

#### Scenario: Served record shape

- **WHEN** the `lres` dataset is read
- **THEN** each record carries the schedule only as `eventStageStartDatesUtc` and the game's `maxRuns`, and no other run, lifecycle or `finished` field
