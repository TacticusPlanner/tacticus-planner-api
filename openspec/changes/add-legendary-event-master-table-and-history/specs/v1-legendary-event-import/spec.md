# Spec Delta

## MODIFIED Requirements

### Requirement: Each V1 event is resolved to a catalog event and reported once

The import SHALL process every V1 event (numeric `LegendaryEventEnum` key) that has a non-null entry in the teams key or the progress key, in ascending numeric order, SHALL map each to the catalog Legendary Event whose raw numeric id matches, and SHALL report exactly one outcome per V1 event with `eventId` (catalog id or null), `v1EventId`, `status` (`Imported`, `Skipped` or `Failed`), `code`, `message`, `teamsImported`, `runInputsImported`, `annotationsImported` and a list of issues. An event absent from the catalog SHALL be `Skipped` with code `event_not_in_catalog`. A finished event served by the catalog SHALL import by the same rules as an active one. An event is `Imported` when at least one team, run input row, annotation or note was written: with code `imported` when at least one team was written, otherwise `inputs_imported`. An event with nothing to write SHALL be `Skipped` with code `plan_already_exists` when the V2 plan already had teams and every V1 run input, annotation and note was already present or kept, otherwise `no_legendary_event_imported`. Each event SHALL be written in its own transaction: a failure on one event SHALL NOT discard the others, and that event reports `Failed` with code `legendary_event_import_failed`.

#### Scenario: V1 event not in the catalog

- **GIVEN** V1 teams for Kharn (V1 id 7), who is not in the catalog
- **WHEN** the part is imported
- **THEN** the outcome for V1 id 7 is `Skipped`, code `event_not_in_catalog`, and the other events are still processed

#### Scenario: Finished V1 event

- **GIVEN** V1 teams for Dante (V1 id 10) and the catalog event `bloodDante` with raw id 10 and `finished` true
- **WHEN** the part is imported
- **THEN** the outcome has `eventId` `bloodDante`, code `imported`, and the teams are created on that plan

#### Scenario: Active V1 event

- **GIVEN** V1 teams for Lysander (V1 id 15) and the catalog event `astarLysander` with raw id 15
- **WHEN** the part is imported
- **THEN** the outcome has `eventId` `astarLysander` and the teams are created on that plan

#### Scenario: Event only in the progress key

- **GIVEN** V1 progress for Farsight (V1 id 13) with an `overview` and no Farsight entry in the teams key
- **WHEN** the part is imported
- **THEN** one outcome is reported for V1 id 13 with `eventId` `tauFarsight`, code `inputs_imported` and `teamsImported` 0
