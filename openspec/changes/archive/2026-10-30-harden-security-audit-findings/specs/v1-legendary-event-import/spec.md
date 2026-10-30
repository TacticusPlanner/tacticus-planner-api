## MODIFIED Requirements

### Requirement: Each V1 event is resolved to a catalog event and reported once

The import SHALL process the V1 events (numeric `LegendaryEventEnum` keys) that have a non-null entry in the teams key or the progress key, in ascending numeric order. It SHALL process at most the first 100 such events; any beyond the first 100 SHALL NOT be processed and get no outcome.

Each processed event SHALL be mapped to the catalog Legendary Event whose raw numeric id matches. Each processed event SHALL get exactly one outcome with:

- `eventId` (the catalog id, or null);
- `v1EventId`;
- `status`: `Imported`, `Skipped` or `Failed`;
- `code`;
- `message`;
- `teamsImported`, `runInputsImported` and `annotationsImported`;
- a list of issues.

The status and code SHALL be decided as follows:

- An event absent from the catalog SHALL be `Skipped` with code `event_not_in_catalog`.
- An event is `Imported` when at least one team, run input row, annotation or note was written. Its code is `imported` when at least one team was written, and `inputs_imported` otherwise.
- An event with nothing to write SHALL be `Skipped`. Its code is `plan_already_exists` when the V2 plan already had teams and every V1 run input, annotation and note was already present or kept, and `no_legendary_event_imported` otherwise.

Each event SHALL be written in its own transaction. A failure on one event SHALL NOT discard the others, and that event reports `Failed` with code `legendary_event_import_failed`.

#### Scenario: Finished V1 event

- **GIVEN** V1 teams for Dante (V1 id 10), who is not in the catalog
- **WHEN** the part is imported
- **THEN** the outcome for V1 id 10 is `Skipped`, code `event_not_in_catalog`, and the other events are still processed

#### Scenario: Active V1 event

- **GIVEN** V1 teams for Lysander (V1 id 15) and the catalog event `astarLysander` with raw id 15
- **WHEN** the part is imported
- **THEN** the outcome has `eventId` `astarLysander` and the teams are created on that plan

#### Scenario: Event only in the progress key

- **GIVEN** V1 progress for Farsight (V1 id 13) with an `overview` and no Farsight entry in the teams key
- **WHEN** the part is imported
- **THEN** one outcome is reported for V1 id 13 with `eventId` `tauFarsight`, code `inputs_imported` and `teamsImported` 0

#### Scenario: More events than the import reads

- **GIVEN** a V1 profile with 101 events that have entries
- **WHEN** the part is imported
- **THEN** 100 outcomes are reported, and the last event is not processed

## ADDED Requirements

### Requirement: Imported teams respect the per-lane cap

After merging, the import SHALL keep at most 50 teams per lane, the cap a plan lane holds (see `legendary-event-plans`). Teams are kept in V1 order. Teams past the cap SHALL be dropped. The event's outcome SHALL then carry one issue `lane_team_limit_reached` per affected lane, with a null `teamName` and the lane id as its value.

#### Scenario: Too many alpha teams

- **GIVEN** a V1 event with 55 distinct alpha teams that all resolve
- **WHEN** the part is imported
- **THEN** 50 alpha teams are created and the outcome lists exactly one issue `lane_team_limit_reached` with value `alpha`

### Requirement: An event's issue list is bounded

An event's outcome SHALL list at most 200 issues. When there would be more, the first 199 SHALL be listed, followed by one issue `issues_truncated` with a null `teamName`. Its value is the number of issues not listed.

#### Scenario: Many dropped units

- **GIVEN** a V1 event whose teams produce 250 issues
- **WHEN** the part is imported
- **THEN** the outcome lists 200 issues, the last being `issues_truncated` with value `51`
