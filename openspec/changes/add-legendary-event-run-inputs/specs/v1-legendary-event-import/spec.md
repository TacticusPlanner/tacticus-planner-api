# Spec Delta

## ADDED Requirements

### Requirement: Run inputs and the paid-options setting are imported

For each V1 event whose progress entry carries an `overview`, the import SHALL turn each non-null entry under key `1`, `2` or `3` into that run's inputs: `regularMissions` and `premiumMissions` clamped to 0..(the event's catalog mission count), `bundle` greater than 0 as `bundlePurchased` true, `ohSoCloseShards` (absent reads as 0) clamped to 0..75 as `closeShards`. A clamped value SHALL be reported as an issue `run_input_clamped` with value `<run>:<field>:<original>`; any other `overview` key SHALL be ignored and reported as `unknown_run` with the key as value; an entry whose four values are all zero or false SHALL NOT be written. A run that already has inputs in V2 SHALL be kept unchanged and reported as `existing_run_inputs_kept` with the run as value. Run inputs SHALL be written whether or not the plan already has teams. `leSettings.showP2POptions` SHALL set `showPaidOptions` only on a plan this import creates, and only when the setting is a boolean; an existing plan's `showPaidOptions` SHALL never change, and an absent setting leaves the V2 default (false). V1 `compactProgress` statuses (including `MaybeClear` and `StopHere`) SHALL NOT be imported. Run-input issues carry a null `teamName`. The event outcome SHALL carry `runInputsImported`, the number of run rows written.

#### Scenario: Overview imported with a clamp

- **GIVEN** V1 `leProgress[14].overview` is `{ "1": { regularMissions: 10, premiumMissions: 0, bundle: 0 }, "2": { regularMissions: 12, premiumMissions: 0, bundle: 1, ohSoCloseShards: 25 } }` and `votanUthar` has 10 regular missions
- **WHEN** the part is imported
- **THEN** Uthar's plan has run 1 `10, 0, false, 0` and run 2 `10, 0, true, 25`, `runInputsImported` is 2, and the outcome lists `run_input_clamped` with value `2:regularMissions:12`

#### Scenario: Run inputs reach a plan that already has teams

- **GIVEN** an earlier import created teams for `astarLysander` and its plan has no run inputs
- **WHEN** the same V1 profile, whose Lysander progress has an `overview` for run 1, is imported again
- **THEN** no team changes, run 1 inputs are written, and the outcome is `Imported` with code `inputs_imported` and `teamsImported` 0

#### Scenario: Existing V2 run inputs are kept

- **GIVEN** the V2 plan already has run 1 inputs
- **WHEN** a V1 `overview` for run 1 is imported
- **THEN** run 1 is unchanged and the outcome lists `existing_run_inputs_kept` with value `1`

#### Scenario: Paid options carried on a new plan

- **GIVEN** V1 `leSettings.showP2POptions` is true and no V2 plan exists for `votanUthar`
- **WHEN** the part imports Uthar
- **THEN** the created plan has `showPaidOptions` true

#### Scenario: Paid options never change an existing plan

- **GIVEN** V1 `leSettings.showP2POptions` is true and the V2 plan for `astarLysander` exists with `showPaidOptions` false
- **WHEN** the part imports Lysander
- **THEN** the plan's `showPaidOptions` stays false

## MODIFIED Requirements

### Requirement: The legendaryEventPlans part reads V1 teams and notes

When the `legendaryEventPlans` part is selected, the import SHALL read `leTeams` (falling back to the older `legendaryEvents3` key), the per-event `notes` and `overview` from `leProgress` (falling back to `legendaryEventsProgress`), and `leSettings.showP2POptions`. When neither a teams key nor a progress key is present the part SHALL report `Skipped` with code `missing_legendary_event_plans`; when the teams key cannot be read (including an event key that is not a number) the part SHALL report `Failed` with code `invalid_legendary_event_plans` and nothing is created. An unreadable progress blob SHALL only drop the notes and run inputs, never fail the teams. An unreadable `leSettings` SHALL be ignored. A null event entry SHALL be ignored and gets no outcome. V1 progress states and statuses, scores, `forceProgress` and settings other than `showP2POptions` SHALL NOT be imported by this part.

#### Scenario: Part not selected

- **WHEN** the import runs without `legendaryEventPlans` selected
- **THEN** the part reports `Skipped` with code `not_selected` and no plan is touched

#### Scenario: No V1 teams

- **GIVEN** a V1 profile with none of `leTeams`, `legendaryEvents3`, `leProgress` and `legendaryEventsProgress`
- **WHEN** the part is imported
- **THEN** it reports `Skipped` with code `missing_legendary_event_plans`

#### Scenario: Legacy key is read

- **GIVEN** a V1 profile with teams only under `legendaryEvents3`
- **WHEN** the part is imported
- **THEN** those teams are imported as if they were under `leTeams`

#### Scenario: Progress without teams

- **GIVEN** a V1 profile with no teams key and `leProgress[14]` carrying an `overview`
- **WHEN** the part is imported
- **THEN** Uthar's run inputs are imported and the part does not report `missing_legendary_event_plans`

### Requirement: Each V1 event is resolved to a catalog event and reported once

The import SHALL process every V1 event (numeric `LegendaryEventEnum` key) present in the teams key or the progress key, in ascending numeric order, SHALL map each to the catalog Legendary Event whose raw numeric id matches, and SHALL report exactly one outcome per V1 event with `eventId` (catalog id or null), `v1EventId`, `status` (`Imported`, `Skipped` or `Failed`), `code`, `message`, `teamsImported`, `runInputsImported` and a list of issues. An event absent from the catalog SHALL be `Skipped` with code `event_not_in_catalog`. An event is `Imported` when at least one team, run input row or note was written: with code `imported` when at least one team was written, otherwise `inputs_imported`. An event with nothing to write SHALL be `Skipped` with code `plan_already_exists` when the V2 plan already had teams and every V1 run input and note was already present or kept, otherwise `no_legendary_event_imported`. Each event SHALL be written in its own transaction: a failure on one event SHALL NOT discard the others, and that event reports `Failed` with code `legendary_event_import_failed`.

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

### Requirement: Existing V2 teams are never replaced

When the profile's plan for the resolved event already has at least one team, the import SHALL NOT create, change or merge any team of that event (the teams of that event are reported through the event's status and code, not as issues); run inputs and notes for that event SHALL still be evaluated by their own rules. Otherwise the import SHALL create the plan when missing and append the resolved teams in V1 order with dense `sortOrder` per lane. In both cases the import SHALL set the plan's `notes` from the V1 event notes when present and not blank, but never overwrite non-empty notes already on the V2 plan (reported as `existing_notes_kept`), and cut notes over 2000 characters on a text-element boundary (reported as `notes_truncated`, value = the original length; both issues carry a null `teamName`). The plan revision SHALL advance as for any write.

#### Scenario: Re-import does not duplicate

- **GIVEN** a previous import created teams, notes and run inputs for `astarLysander`
- **WHEN** the same V1 profile is imported again
- **THEN** the `astarLysander` outcome is `Skipped` with code `plan_already_exists`, its team count is unchanged and no run input changes

#### Scenario: Notes carried

- **GIVEN** V1 `leProgress[15].notes` is "Save tokens for beta"
- **WHEN** the part imports Lysander's teams
- **THEN** the plan's `notes` is "Save tokens for beta"

#### Scenario: Notes reach a plan with teams but no notes

- **GIVEN** the V2 plan for `astarLysander` has teams and null notes, and V1 notes are "Save tokens for beta"
- **WHEN** the part is imported
- **THEN** no team changes, the plan's `notes` becomes "Save tokens for beta", and the outcome is `Imported` with code `inputs_imported`
