# Spec Delta

## MODIFIED Requirements

### Requirement: Teams, units and objectives are resolved by the documented rules

For each V1 team the import SHALL: use the trimmed V1 `name` cut to 60 characters, or `Team N` (its 1-based V1 position) when blank; use `teams` when non-empty, otherwise synthesise teams from the legacy `alpha`/`beta`/`gamma` maps; map `section` to the lane as-is and skip a team with an unknown lane (`unknown_lane`); resolve unit ids from `charSnowprintIds`, then `charactersIds`, then embedded `characters[].snowprintId`, as a catalog unit id, through the V1 rename aliases, or by catalog character name (case-insensitive, V1's legacy `charactersIds` held names), then require the unit in the catalog (`unknown_unit`, unit dropped) and in the lane's `availableUnitIds` (`unit_not_allowed_on_lane`, unit dropped); keep a unit that resolves more than once only at its first position and report each repeat (`duplicate_unit`); keep the first five remaining units when more resolve (`team_truncated`); skip a team with no remaining unit (`empty_team`) or with only one or two remaining units (`team_too_small`, value = the remaining count); resolve each `restrictionsIds` entry against the lane's objectives by regenerated V1 display name or catalog name, case-insensitively after whitespace normalisation with the known legacy spellings, dropping unresolved entries (`unknown_objective`); carry `expectedBattleClears` as a `manual` depth clamped to the lane's battle count, stored under the profile's synced current run for the event (run 1 when the account has no synced entry) and omitted when not positive; merge teams within one lane whose member sets are identical, unioning objectives, keeping the first team's name and the first positive depth, and reporting a later team's differing positive depth as discarded (`conflicting_depth_discarded`, value = the discarded depth) alongside `duplicate_team_merged`. Every drop, truncation or merge SHALL appear as an issue `(code, teamName, value)` on the event's outcome. A team with zero resolved objectives SHALL still be imported. A null entry in any V1 array (a null team, a null unit reference or embedded character, a null objective name) SHALL be skipped without failing the part; a null unit or objective is reported as `unknown_unit` or `unknown_objective` with a null value (a null embedded character reports `unknown_unit` with an empty value). Before this part runs, the import SHALL sync the profile's player data when it has no snapshot yet and a Tacticus API key is available, so a first import stores depths under the current run.

#### Scenario: Objective names resolve across V1 and catalog spellings

- **GIVEN** a V1 team with `restrictionsIds` `["Min 5 hits", "No Resiliant", "Melee"]` on a lane whose objectives are `Min 5 Hits` (MinHits 5), `No Resilient` (Trait Resilient exclude) and `Melee` (AttackType melee)
- **WHEN** the team is imported
- **THEN** all three objectives resolve and the team covers those three indexes with no issue

#### Scenario: Unit dropped but team kept

- **GIVEN** a V1 team with units `[u1, u2, u3, unknownX]` where `u1`, `u2` and `u3` are allowed on the lane
- **WHEN** the team is imported
- **THEN** the team is created with members `[u1, u2, u3]` and the outcome lists an issue `unknown_unit` with value `unknownX` and the team's name

#### Scenario: Repeated unit kept once

- **GIVEN** a V1 team whose references resolve to `[u1, u2, u3, u1]`
- **WHEN** the team is imported
- **THEN** the team's members are `[u1, u2, u3]` and the outcome lists an issue `duplicate_unit` with value `u1`

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

#### Scenario: A V1 team of two units is skipped

- **GIVEN** a V1 team of two units that both resolve
- **WHEN** the import runs
- **THEN** no team is created and the outcome lists an issue `team_too_small` for that team name with value 2

#### Scenario: Units dropped below three

- **GIVEN** a V1 team of three units of which one is not allowed on the lane
- **WHEN** the import runs
- **THEN** the dropped unit is reported (`unit_not_allowed_on_lane`) and the team is skipped with `team_too_small` value 2
