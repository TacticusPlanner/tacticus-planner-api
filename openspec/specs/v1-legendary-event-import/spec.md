# v1-legendary-event-import Specification

## Purpose

Defines the `legendaryEventPlans` part of the V1 profile import: which V1 keys are read, how V1 events, lanes, units and objectives are resolved onto V2 catalog ids, what is created, and the per-event outcome report with stable codes.

## Requirements

### Requirement: The legendaryEventPlans part reads V1 teams and notes

When the `legendaryEventPlans` part is selected, the import SHALL read `leTeams` (falling back to the older `legendaryEvents3` key), the per-event `notes` and `overview` from `leProgress` (falling back to `legendaryEventsProgress`), and `leSettings.showP2POptions`. When neither a teams key nor a progress key is present the part SHALL report `Skipped` with code `missing_legendary_event_plans`; when the teams key cannot be read (including an event key that is not a number) the part SHALL report `Failed` with code `invalid_legendary_event_plans` and nothing is created. An unreadable progress blob SHALL only drop the notes, run inputs and annotations, never fail the teams. An unreadable `leSettings` SHALL be ignored. An event whose entries are null (or absent) in both the teams key and the progress key SHALL be ignored and gets no outcome. V1 progress states and statuses other than the Maybe clear and Stop here marks (see "Maybe clear and Stop here marks are imported as annotations"), scores, `forceProgress` and settings other than `showP2POptions` SHALL NOT be imported by this part.

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

The import SHALL process every V1 event (numeric `LegendaryEventEnum` key) that has a non-null entry in the teams key or the progress key, in ascending numeric order, SHALL map each to the catalog Legendary Event whose raw numeric id matches, and SHALL report exactly one outcome per V1 event with `eventId` (catalog id or null), `v1EventId`, `status` (`Imported`, `Skipped` or `Failed`), `code`, `message`, `teamsImported`, `runInputsImported`, `annotationsImported` and a list of issues. An event absent from the catalog SHALL be `Skipped` with code `event_not_in_catalog`. An event is `Imported` when at least one team, run input row, annotation or note was written: with code `imported` when at least one team was written, otherwise `inputs_imported`. An event with nothing to write SHALL be `Skipped` with code `plan_already_exists` when the V2 plan already had teams and every V1 run input, annotation and note was already present or kept, otherwise `no_legendary_event_imported`. Each event SHALL be written in its own transaction: a failure on one event SHALL NOT discard the others, and that event reports `Failed` with code `legendary_event_import_failed`.

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

### Requirement: Teams, units and objectives are resolved by the documented rules

For each V1 team the import SHALL: use the trimmed V1 `name` cut to 60 characters, or `Team N` (its 1-based V1 position) when blank; use `teams` when non-empty, otherwise synthesise teams from the legacy `alpha`/`beta`/`gamma` maps; map `section` to the lane as-is and skip a team with an unknown lane (`unknown_lane`); resolve unit ids from `charSnowprintIds`, then `charactersIds`, then embedded `characters[].snowprintId`, as a catalog unit id, through the V1 rename aliases, or by catalog character name (case-insensitive, V1's legacy `charactersIds` held names), then require the unit in the catalog (`unknown_unit`, unit dropped) and in the lane's `availableUnitIds` (`unit_not_allowed_on_lane`, unit dropped); keep a unit that resolves more than once only at its first position and report each repeat (`duplicate_unit`); keep the first five remaining units when more resolve (`team_truncated`); skip a team with no remaining unit (`empty_team`); resolve each `restrictionsIds` entry against the lane's objectives by regenerated V1 display name or catalog name, case-insensitively after whitespace normalisation with the known legacy spellings, dropping unresolved entries (`unknown_objective`); carry `expectedBattleClears` as a `manual` depth clamped to the lane's battle count, stored under the profile's synced current run for the event (run 1 when the account has no synced entry) and omitted when not positive; merge teams within one lane whose member sets are identical, unioning objectives, keeping the first team's name and the first positive depth, and reporting a later team's differing positive depth as discarded (`conflicting_depth_discarded`, value = the discarded depth) alongside `duplicate_team_merged`. Every drop, truncation or merge SHALL appear as an issue `(code, teamName, value)` on the event's outcome. A team with zero resolved objectives SHALL still be imported. A null entry in any V1 array (a null team, a null unit reference or embedded character, a null objective name) SHALL be skipped without failing the part; a null unit or objective is reported as `unknown_unit` or `unknown_objective` with a null value (a null embedded character reports `unknown_unit` with an empty value). Before this part runs, the import SHALL sync the profile's player data when it has no snapshot yet and a Tacticus API key is available, so a first import stores depths under the current run.

#### Scenario: Objective names resolve across V1 and catalog spellings

- **GIVEN** a V1 team with `restrictionsIds` `["Min 5 hits", "No Resiliant", "Melee"]` on a lane whose objectives are `Min 5 Hits` (MinHits 5), `No Resilient` (Trait Resilient exclude) and `Melee` (AttackType melee)
- **WHEN** the team is imported
- **THEN** all three objectives resolve and the team covers those three indexes with no issue

#### Scenario: Unit dropped but team kept

- **GIVEN** a V1 team with units `[u1, u2, unknownX]` where `u1` and `u2` are allowed on the lane
- **WHEN** the team is imported
- **THEN** the team is created with members `[u1, u2]` and the outcome lists an issue `unknown_unit` with value `unknownX` and the team's name

#### Scenario: Repeated unit kept once

- **GIVEN** a V1 team whose references resolve to `[u1, u2, u1]`
- **WHEN** the team is imported
- **THEN** the team's members are `[u1, u2]` and the outcome lists an issue `duplicate_unit` with value `u1`

#### Scenario: Merged teams keep the first depth

- **GIVEN** two Alpha teams with identical members, the first with `expectedBattleClears` 7 and the second with 9
- **WHEN** they are imported
- **THEN** one team is created with the run depth 7, and the outcome lists `duplicate_team_merged` and `conflicting_depth_discarded` with value 9

#### Scenario: Legacy alias resolves

- **GIVEN** a V1 team naming the unit `Patermine` by legacy character name
- **WHEN** the team is imported
- **THEN** the member is `genesPatriarch`, provided the lane allows it

#### Scenario: Empty team skipped

- **GIVEN** a V1 team whose every unit is unknown
- **WHEN** the team is imported
- **THEN** no team is created and the outcome lists an issue `empty_team` for that team name

#### Scenario: Depth clamped

- **GIVEN** a V1 team with `expectedBattleClears` 25 on an 18-battle lane and the profile's synced progress says the event is in run 2
- **WHEN** the team is imported
- **THEN** the team's `runDepths` is `[{run 2, 18, manual}]`

#### Scenario: Depth without a synced run

- **GIVEN** a V1 team with `expectedBattleClears` 5 and no synced progress entry for the event
- **WHEN** the team is imported
- **THEN** the team's `runDepths` is `[{run 1, 5, manual}]`

### Requirement: Existing V2 teams are never replaced

When the profile's plan for the resolved event already has at least one team, the import SHALL NOT create, change or merge any team of that event (the teams of that event are reported through the event's status and code, not as issues); run inputs, annotations and notes for that event SHALL still be evaluated by their own rules. Otherwise the import SHALL create the plan when missing and append the resolved teams in V1 order with dense `sortOrder` per lane. In both cases the import SHALL set the plan's `notes` from the V1 event notes when present and not blank, but never overwrite non-empty notes already on the V2 plan (reported as `existing_notes_kept`), and cut notes over 2000 characters on a text-element boundary (reported as `notes_truncated`, value = the original length; both issues carry a null `teamName`). The plan revision SHALL advance as for any write.

#### Scenario: Re-import does not duplicate

- **GIVEN** a previous import created teams, notes, run inputs and annotations for `astarLysander`
- **WHEN** the same V1 profile is imported again
- **THEN** the `astarLysander` outcome is `Skipped` with code `plan_already_exists`, its team count is unchanged and no run input or annotation changes

#### Scenario: Notes carried

- **GIVEN** V1 `leProgress[15].notes` is "Save tokens for beta"
- **WHEN** the part imports Lysander's teams
- **THEN** the plan's `notes` is "Save tokens for beta"

#### Scenario: Notes reach a plan with teams but no notes

- **GIVEN** the V2 plan for `astarLysander` has teams and null notes, and V1 notes are "Save tokens for beta"
- **WHEN** the part is imported
- **THEN** no team changes, the plan's `notes` becomes "Save tokens for beta", and the outcome is `Imported` with code `inputs_imported`

### Requirement: The part result summarises the events

The part SHALL report `Imported` when at least one event imported; otherwise `Failed` with code `legendary_event_import_failed` when at least one event failed (whether or not others were skipped); otherwise `Skipped` with code `no_legendary_event_imported` when every event had nothing to import, or `legendary_events_skipped` when at least one event was skipped as `plan_already_exists` or `event_not_in_catalog`. The response SHALL carry the per-event outcomes in `legendaryEventOutcomes`, in V1 key order.

#### Scenario: Skipped and failed without an import

- **GIVEN** one event is not in the catalog and another fails to write
- **WHEN** the part runs
- **THEN** the part is `Failed` with code `legendary_event_import_failed` and both per-event outcomes are reported

#### Scenario: Mixed result

- **GIVEN** one event imports, one is not in the catalog and one already exists
- **WHEN** the part runs
- **THEN** the part is `Imported` and `legendaryEventOutcomes` has three entries with codes `imported`, `event_not_in_catalog` and `plan_already_exists`

### Requirement: Run inputs and the paid-options setting are imported

For each V1 event whose progress entry carries an `overview`, the import SHALL turn each non-null entry under key `1`, `2` or `3` into that run's inputs: `regularMissions` and `premiumMissions` clamped to 0..(the event's catalog mission count), `bundle` greater than 0 as `bundlePurchased` true, `ohSoCloseShards` (absent reads as 0) clamped to 0..75 as `closeShards`. A clamped value SHALL be reported as an issue `run_input_clamped` with value `<run>:<field>:<original>`; any other `overview` key SHALL be ignored and reported as `unknown_run` with the key as value; an entry whose four values are all zero or false SHALL NOT be written. A run that already has inputs in V2 SHALL be kept unchanged and reported as `existing_run_inputs_kept` with the run as value. Run inputs SHALL be written whether or not the plan already has teams. `leSettings.showP2POptions` SHALL set `showPaidOptions` only on a plan this import creates, and only when the setting is a boolean; an existing plan's `showPaidOptions` SHALL never change, and an absent setting leaves the V2 default (false). Run-input issues carry a null `teamName`. The event outcome SHALL carry `runInputsImported`, the number of run rows written.

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

### Requirement: Maybe clear and Stop here marks are imported as annotations

For each V1 event, the import SHALL read the progress entry's `compactProgress`, or its legacy `battlesProgress` when `compactProgress` is absent, and turn every V1 `MaybeClear` status (2) into a `maybeClear` annotation and every `StopHere` status (3) into `stopHere`; in `battlesProgress` a requirement without a `status` whose `state` is 2 (blocked) SHALL count as `StopHere`. No other status or state SHALL be imported. The V1 lane key SHALL be the lane (`alpha`, `beta`, `gamma`), else the entries are skipped with issue `unknown_lane` (value = the key); the V1 battle index SHALL be the `battleIndex`, else skipped with `unknown_battle` (value `<lane>:<index>`); the V1 requirement id SHALL map to `objectiveId` 0 for `_defeatAll`, `_killPoints` and `_highScore`, and to the matched lane objective's `index + 1` for an objective name (matched as team objectives are), else skipped with `unknown_objective` (value `<lane>:<name>`). When several V1 marks land on one cell, `stopHere` SHALL win. A cell already annotated in V2 SHALL be kept, and the number of kept cells reported once as `existing_annotations_kept`. Annotations SHALL be written whether or not the plan already has teams, and whatever the synced progress says. Annotation issues carry a null `teamName`. The event outcome SHALL carry `annotationsImported`, the number of annotations written.

#### Scenario: Compact statuses imported

- **GIVEN** V1 `leProgress[15].compactProgress.alpha` has `{ "Flying": { states: [], statuses: { "6": 3 } }, "_defeatAll": { states: [], statuses: { "8": 2 } }, "_killPoints": { states: [], statuses: { "8": 3 } } }` and Lysander Alpha's `Flying` objective has catalog index 2
- **WHEN** the part imports Lysander
- **THEN** the plan has annotations alpha/6/3 `stopHere` and alpha/8/0 `stopHere` (the kill-score mark wins over defeat-all's `maybeClear`), and `annotationsImported` is 2

#### Scenario: Legacy blocked state

- **GIVEN** V1 progress with only `battlesProgress` `[{ trackId: "beta", battleIndex: 2, requirements: [{ id: "_defeatAll", state: 2 }] }]`
- **WHEN** the part is imported
- **THEN** the plan has annotation beta/2/0 `stopHere`

#### Scenario: Unknown objective name

- **GIVEN** a V1 mark on requirement "Psyker" in alpha, which matches no Lysander Alpha objective
- **WHEN** the part is imported
- **THEN** that mark is skipped and the outcome lists `unknown_objective` with value `alpha:Psyker`

#### Scenario: Existing V2 annotation kept

- **GIVEN** the V2 plan has alpha/6/3 `maybeClear` and V1 marks the same cell `StopHere`
- **WHEN** the part is imported
- **THEN** alpha/6/3 stays `maybeClear` and the outcome lists `existing_annotations_kept` with value `1`
