# v1-legendary-event-import Specification

## Purpose

Defines the `legendaryEventPlans` part of the V1 profile import: which V1 keys are read, how V1 events, lanes, units and objectives are resolved onto V2 catalog ids, what is created, and the per-event outcome report with stable codes.

## ADDED Requirements

### Requirement: The legendaryEventPlans part reads V1 teams and notes

When the `legendaryEventPlans` part is selected, the import SHALL read `leTeams` (falling back to the older `legendaryEvents3` key) and the per-event `notes` from `leProgress` (falling back to `legendaryEventsProgress`). When neither teams key is present the part SHALL report `Skipped` with code `missing_legendary_event_plans`; when the keys cannot be read the part SHALL report `Failed` with code `invalid_legendary_event_plans` and nothing is created. V1 progress states, scores, `forceProgress`, run inputs and settings SHALL NOT be imported by this part.

#### Scenario: Part not selected

- **WHEN** the import runs without `legendaryEventPlans` selected
- **THEN** the part reports `Skipped` with code `not_selected` and no plan is touched

#### Scenario: No V1 teams

- **GIVEN** a V1 profile with neither `leTeams` nor `legendaryEvents3`
- **WHEN** the part is imported
- **THEN** it reports `Skipped` with code `missing_legendary_event_plans`

#### Scenario: Legacy key is read

- **GIVEN** a V1 profile with teams only under `legendaryEvents3`
- **WHEN** the part is imported
- **THEN** those teams are imported as if they were under `leTeams`

### Requirement: Each V1 event is resolved to a catalog event and reported once

The import SHALL map each V1 event (numeric `LegendaryEventEnum` key) to the catalog Legendary Event whose raw numeric id matches, and SHALL report exactly one outcome per V1 event with `eventId` (catalog id or null), `v1EventId`, `status` (`Imported`, `Skipped` or `Failed`), `code`, `message`, `teamsImported` and a list of issues. An event absent from the catalog SHALL be `Skipped` with code `event_not_in_catalog`. Each event SHALL be written in its own transaction: a failure on one event SHALL NOT discard the others, and that event reports `Failed` with code `legendary_event_import_failed`.

#### Scenario: Finished V1 event

- **GIVEN** V1 teams for Dante (V1 id 10), who is not in the catalog
- **WHEN** the part is imported
- **THEN** the outcome for V1 id 10 is `Skipped`, code `event_not_in_catalog`, and the other events are still processed

#### Scenario: Active V1 event

- **GIVEN** V1 teams for Lysander (V1 id 15) and the catalog event `astarLysander` with raw id 15
- **WHEN** the part is imported
- **THEN** the outcome has `eventId` `astarLysander` and the teams are created on that plan

### Requirement: Teams, units and objectives are resolved by the documented rules

For each V1 team the import SHALL: use `teams` when non-empty, otherwise synthesise teams from the legacy `alpha`/`beta`/`gamma` maps; map `section` to the lane as-is and skip a team with an unknown lane (`unknown_lane`); resolve unit ids from `charSnowprintIds`, then `charactersIds`, then embedded `characters[].snowprintId`, through the V1 rename aliases, then require the unit in the catalog (`unknown_unit`, unit dropped) and in the lane's `availableUnitIds` (`unit_not_allowed_on_lane`, unit dropped); keep the first five units when more resolve (`team_truncated`); skip a team with no remaining unit (`empty_team`); resolve each `restrictionsIds` entry against the lane's objectives by regenerated V1 display name or catalog name, case-insensitively after whitespace normalisation with the known legacy spellings, dropping unresolved entries (`unknown_objective`); carry `expectedBattleClears` as a `manual` depth clamped to the lane's battle count and null when not positive; merge teams within one lane whose member sets are identical, unioning objectives (`duplicate_team_merged`). Every drop, truncation or merge SHALL appear as an issue `(code, teamName, value)` on the event's outcome. A team with zero resolved objectives SHALL still be imported.

#### Scenario: Objective names resolve across V1 and catalog spellings

- **GIVEN** a V1 team with `restrictionsIds` `["Min 5 hits", "No Resiliant", "Melee"]` on a lane whose objectives are `Min 5 Hits` (MinHits 5), `No Resilient` (Trait Resilient exclude) and `Melee` (AttackType melee)
- **WHEN** the team is imported
- **THEN** all three objectives resolve and the team covers those three indexes with no issue

#### Scenario: Unit dropped but team kept

- **GIVEN** a V1 team with units `[u1, u2, unknownX]` where `u1` and `u2` are allowed on the lane
- **WHEN** the team is imported
- **THEN** the team is created with members `[u1, u2]` and the outcome lists an issue `unknown_unit` with value `unknownX` and the team's name

#### Scenario: Legacy alias resolves

- **GIVEN** a V1 team naming the unit `Patermine` by legacy character name
- **WHEN** the team is imported
- **THEN** the member is `genesPatriarch`, provided the lane allows it

#### Scenario: Empty team skipped

- **GIVEN** a V1 team whose every unit is unknown
- **WHEN** the team is imported
- **THEN** no team is created and the outcome lists an issue `empty_team` for that team name

#### Scenario: Depth clamped

- **GIVEN** a V1 team with `expectedBattleClears` 25 on an 18-battle lane
- **WHEN** the team is imported
- **THEN** the team has `expectedBattleClears` 18 with source `manual`

### Requirement: Existing V2 teams are never replaced

When the profile's plan for the resolved event already has at least one team, the event SHALL be `Skipped` with code `plan_already_exists` and nothing on that plan changes, including its notes. Otherwise the import SHALL create the plan when missing, set its `notes` from the V1 event notes when present and non-empty, and append the resolved teams in V1 order with dense `sortOrder` per lane. The plan revision SHALL advance as for any write.

#### Scenario: Re-import does not duplicate

- **GIVEN** a previous import created teams for `astarLysander`
- **WHEN** the same V1 profile is imported again
- **THEN** the `astarLysander` outcome is `Skipped` with code `plan_already_exists` and its team count is unchanged

#### Scenario: Notes carried

- **GIVEN** V1 `leProgress[15].notes` is "Save tokens for beta"
- **WHEN** the part imports Lysander's teams
- **THEN** the plan's `notes` is "Save tokens for beta"

### Requirement: The part result summarises the events

The part SHALL report `Imported` when at least one event imported, `Skipped` with code `no_legendary_event_imported` when every event was skipped, and `Failed` when every event failed. The response SHALL carry the per-event outcomes in `legendaryEventOutcomes`, in V1 key order.

#### Scenario: Mixed result

- **GIVEN** one event imports, one is not in the catalog and one already exists
- **WHEN** the part runs
- **THEN** the part is `Imported` and `legendaryEventOutcomes` has three entries with codes `imported`, `event_not_in_catalog` and `plan_already_exists`
