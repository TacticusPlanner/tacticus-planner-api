# legendary-event-run-schedule Specification

## Purpose
Defines the run start dates the game catalog serves for each Legendary Event, so a client can tell which of the event's three runs a start belongs to, which runs have ended and which comes next, without relying on the synced run number (which the game omits between runs).

## Requirements

### Requirement: Served run start dates are indexed by run

Every record of the served `lres` dataset SHALL carry `eventStageStartDatesUtc`, an array of ISO 8601 UTC instants (`yyyy-MM-ddTHH:mm:ssZ`) whose index is the run number minus one: element 0 starts run 1, element 1 run 2, element 2 run 3. Only announced runs are listed, so the array has 0 to 3 elements and never a gap: a later run is not listed unless every earlier run is.

#### Scenario: Uthar after two runs

- **GIVEN** the raw `lres-votanuthar.json` has `runStartDatesUtc` of `["Sun, 08 March 2026 00:00:00 GMT", "Sun, 04 October 2026 00:00:00 GMT"]`
- **WHEN** the `lres` dataset is served
- **THEN** the `votanUthar` record carries `eventStageStartDatesUtc` `["2026-03-08T00:00:00Z", "2026-10-04T00:00:00Z"]`, so run 1 starts on 8 March and run 2 on 4 October; run 3 is not announced

#### Scenario: Upcoming first run

- **GIVEN** the raw `lres-lostabile.json` has `runStartDatesUtc` of `["Sun, 08 November 2026 00:00:00 GMT"]`
- **WHEN** the dataset is served
- **THEN** the record carries `["2026-11-08T00:00:00Z"]`, which the client reads as run 1 starting on 8 November

### Requirement: The served array is derived from the raw run starts

The served `eventStageStartDatesUtc` SHALL be the raw file's `runStartDatesUtc` with each entry normalised to ISO 8601 UTC, in the same order. A raw file with an absent or empty `runStartDatesUtc` SHALL serve an empty array. The raw files SHALL NOT carry `eventStage`, `nextEventDate`, `nextEventDateUtc` or `finished`, since each is derivable from `runStartDatesUtc`.

#### Scenario: Event with no announced run

- **GIVEN** a raw file whose `runStartDatesUtc` is empty or absent
- **WHEN** the dataset is served
- **THEN** the record carries `eventStageStartDatesUtc` `[]`

### Requirement: The server sends no derived run information

The server SHALL NOT send a run number, a lifecycle state, a "next run", an "ended runs" value or a `finished` flag for an event; the client derives them from `eventStageStartDatesUtc`, `maxRuns` and the current instant (an event is finished once all `maxRuns` run starts are listed and the last run's 7 days have passed). `maxRuns` is the game's run count (`heroEventMaxRuns`), not state derived from the clock.

#### Scenario: Served record shape

- **WHEN** the `lres` dataset is read
- **THEN** each record carries the schedule only as `eventStageStartDatesUtc` and the game's `maxRuns`, and no other run, lifecycle or `finished` field

### Requirement: The loader rejects invalid run start dates

The catalog loader SHALL reject a raw `lres-*.json` file whose `runStartDatesUtc` has more than three entries, contains an entry that cannot be parsed as a UTC instant, is not strictly ascending, or lists two consecutive starts less than 7 days apart (the runs would overlap). The error SHALL name the event, the source file and the offending entry. A file with an absent or empty `runStartDatesUtc` SHALL load.

#### Scenario: Four run starts

- **GIVEN** a raw file listing four run starts
- **WHEN** the catalog is loaded
- **THEN** loading fails with an error naming the event, its file and "more than 3 run starts"

#### Scenario: Overlapping runs

- **GIVEN** a raw file whose run 2 starts 5 days after run 1
- **WHEN** the catalog is loaded
- **THEN** loading fails naming run 2 as overlapping run 1

#### Scenario: Unparseable entry

- **GIVEN** a raw file whose second entry is `"soon"`
- **WHEN** the catalog is loaded
- **THEN** loading fails naming the entry at index 1

#### Scenario: Out of order

- **GIVEN** a raw file whose run 2 starts before run 1
- **WHEN** the catalog is loaded
- **THEN** loading fails naming the order violation at index 1
