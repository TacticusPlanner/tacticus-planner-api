## Purpose

Defines the `teams` part of the V1 profile import: how V1 reusable teams become V2 teams, how a re-run behaves, and the per-team outcome report with stable codes.

## ADDED Requirements

### Requirement: The teams part reads V1 teams2

The `POST /api/v1/account/v1-import` selection SHALL include `teams`, default `false`. When selected, the import SHALL read `teams2` and SHALL NOT read V1's legacy `teams`, `warDefense2` or `warOffense2`. The part SHALL report `Skipped` with code `not_selected` when not selected, `Skipped` with code `missing_teams` when `teams2` is absent or empty, and `Failed` with code `invalid_teams` when `teams2` is not an array, in which case nothing is created. An unreadable `teams2` SHALL NOT fail any other part. Teams SHALL be processed in V1 `priority` order, then V1 array order, and at most the first 100 SHALL be processed; any beyond that get no outcome.

#### Scenario: Part not selected

- **WHEN** the import runs without `teams` selected
- **THEN** the part reports `Skipped` with code `not_selected` and no team is touched

#### Scenario: No V1 teams

- **GIVEN** a V1 profile whose `teams2` is empty
- **WHEN** the part is imported
- **THEN** it reports `Skipped` with code `missing_teams`

#### Scenario: Unreadable teams

- **GIVEN** a V1 profile whose `teams2` is an object
- **WHEN** the import runs with goals and teams selected
- **THEN** the teams part reports `Failed` with code `invalid_teams` and the goals part runs normally

### Requirement: Each V1 team becomes a V2 team

Units SHALL be resolved onto catalog ids by the same id, alias and name rules as the Legendary Event import. V1 `chars` SHALL resolve to characters and `mows` and `incursionMows` to MoWs. An unresolved unit SHALL be dropped and reported. A unit repeated within `chars`, `mows` or `incursionMows` SHALL be kept once and each repeat reported. `flexStartIndex` SHALL be the number of kept characters before V1 `flexIndex`, or null when no kept character follows it or V1 `flexIndex` is out of range. When no kept character comes before V1 `flexIndex`, including V1 `flexIndex` 0 (an all-flex V1 team), every character SHALL become core and the dropped flex section SHALL be reported, because a V2 team has at least one core character. V1 flags SHALL map to modes:

| V1 | V2 |
|---|---|
| `warOffense` | `warOffense` |
| `warDefense` | `warDefense` |
| `raid` | `guildRaid` |
| `ta` | `tournamentArena` |
| `horde` | `horde` |
| `campaign` | `campaign` |
| `incursion` | `incursion` |

V1 `campaignStoryline` SHALL map to these campaign groups, and any other value SHALL be reported and leave the campaign empty:

| V1 | V2 |
|---|---|
| `indomitus` | `campaign1` |
| `indomitusMirror` | `mirror1` |
| `fallOfCadia` | `campaign2` |
| `fallOfCadiaMirror` | `mirror2` |
| `octarius` | `campaign3` |
| `octariusMirror` | `mirror3` |
| `saimHann` | `campaign4` |
| `saimHannMirror` | `mirror4` |
| `adMech` | `eventCampaign1` |
| `tyranids` | `eventCampaign2` |
| `tau` | `eventCampaign3` |
| `deathGuard` | `eventCampaign4` |
| `sisters` | `eventCampaign5` |
| `darkAngels` | `eventCampaign6` |
| `bloodAngels` | `eventCampaign7` |

A name longer than 40 characters SHALL be cut to 40, and notes longer than 300 characters SHALL be cut to 300; each SHALL be reported. Notes that are blank SHALL be imported as null.

#### Scenario: Unknown unit before the flex section

- **GIVEN** a V1 team with `chars` `["ultraTigurius", "notAUnit", "ultraTitus", "ultraApothecary"]` and `flexIndex` 2
- **WHEN** it is imported
- **THEN** `characterIds` is `["ultraTigurius", "ultraTitus", "ultraApothecary"]`, `flexStartIndex` is 1, and `notAUnit` is reported on its outcome

#### Scenario: All-flex V1 team

- **GIVEN** a V1 team with `chars` `["ultraTigurius", "ultraTitus"]` and `flexIndex` 0
- **WHEN** it is imported
- **THEN** `characterIds` is `["ultraTigurius", "ultraTitus"]`, `flexStartIndex` is null, and `flex_dropped` is reported on its outcome

#### Scenario: Repeated unit

- **GIVEN** a V1 team with `chars` `["ultraTigurius", "ultraTigurius", "ultraTitus", "ultraApothecary"]` and `flexIndex` 3
- **WHEN** it is imported
- **THEN** `characterIds` is `["ultraTigurius", "ultraTitus", "ultraApothecary"]`, `flexStartIndex` is 2, and `duplicate_unit` with value `ultraTigurius` is reported on its outcome

#### Scenario: Campaign storyline

- **GIVEN** a V1 team with `campaign: true` and `campaignStoryline: "tau"`
- **WHEN** it is imported
- **THEN** the team has mode `campaign` and `campaignGroupId` `eventCampaign3`

### Requirement: Imported teams keep V1's content even when it breaks a rule

A resolved V1 team SHALL be imported even when it breaks a team rule, and SHALL NOT be repaired. Its broken rules SHALL be listed on its outcome and SHALL appear as `issues` when it is read. A V1 team with a usable name and no resolved units SHALL NOT be imported. An entry without a usable name, or whose `chars` is not an array, SHALL NOT be imported.

#### Scenario: V1 team with several MoWs

- **GIVEN** a V1 `warOffense` team with 2 MoWs
- **WHEN** it is imported
- **THEN** it is imported with both MoWs and its outcome lists `tooManyMows`

#### Scenario: V1 Incursion team outside the allowed factions

- **GIVEN** a V1 Incursion team for `orksRukkatrukk` with a Sisterhood character, which V1's alliance-wide check allowed
- **WHEN** it is imported
- **THEN** it is imported with that character and its outcome lists `factionNotAllowed`

#### Scenario: Empty V1 team

- **GIVEN** a V1 team with a name and no units
- **WHEN** it is imported
- **THEN** no team is created and its outcome is `Skipped` with code `no_units`

### Requirement: Re-running the import never changes existing V2 teams

Each imported team SHALL store an import key: its original V1 name, trimmed and before shortening, compared ignoring case. The key SHALL never change, including when the team is renamed, and SHALL be deleted with the team. A V1 team whose key matches an existing team of the profile, or a team earlier in the same import, SHALL be skipped with code `already_imported`, even when the lineups differ. Otherwise, a V1 team whose final name matches an existing team of the profile, ignoring case, SHALL be skipped with code `duplicate_name`. Neither SHALL use room under the limit. The import SHALL NOT update, reorder or delete an existing team. Imported teams SHALL be added after the profile's existing teams. Only room under the team limit SHALL be used; teams beyond it SHALL be skipped with code `team_limit_reached`. A team deleted in V2 takes its key with it, so a later re-run SHALL import that V1 team again.

Rationale: V1 has no team ids, and V1 War Defense refers to teams by name. A later War Defense import resolves those names through the import key, so it still finds a team the player renamed or whose name was shortened.

#### Scenario: Re-run

- **GIVEN** an import that created 5 teams
- **WHEN** the import runs again with the same V1 data
- **THEN** all 5 outcomes are `Skipped` with code `already_imported` and the V2 teams are unchanged

#### Scenario: Re-run after a rename

- **GIVEN** an import created "GW Ultras", which the player then renamed "Ultra D1"
- **WHEN** the import runs again with the same V1 data
- **THEN** "GW Ultras" is `Skipped` with code `already_imported` and no second copy is created

#### Scenario: Name taken by a team made in V2

- **GIVEN** the player created a team "Horde" in V2, and V1 has a team "horde" never imported
- **WHEN** the import runs
- **THEN** the V1 team is `Skipped` with code `duplicate_name`

#### Scenario: Re-run after a delete

- **GIVEN** an import created "GW Ultras", which the player then deleted
- **WHEN** the import runs again with the same V1 data
- **THEN** "GW Ultras" is imported again

#### Scenario: Partial room

- **GIVEN** a profile with 18 teams and 4 importable V1 teams with new names
- **WHEN** the import runs
- **THEN** the first 2 are imported, in V1 priority order, and the other 2 are `Skipped` with code `team_limit_reached`

### Requirement: Each V1 team reports one outcome

The response SHALL carry the part result as `teams` and SHALL carry `teamOutcomes`, with one outcome per processed V1 team:

- `name` (string or null): the V1 name
- `status`: `Imported`, `Skipped` or `Failed`
- `code` (string or null): null when imported, otherwise `already_imported`, `duplicate_name`, `team_limit_reached`, `no_units` or `invalid_team`
- `teamId` (string or null): the created team's id
- `issues` (array of `{ code, value }`): `unit_not_recognised` with the V1 reference, `campaign_not_recognised` with the V1 value, `duplicate_unit` with the repeated V1 reference, `flex_dropped`, `name_shortened`, `notes_shortened`, and each team rule code the imported team breaks, with a null `value`

The part SHALL report `Imported` when at least one team was imported, `Failed` with code `team_import_failed` when none was and at least one failed, and otherwise `Skipped` with code `teams_skipped`.

#### Scenario: Nothing new on a re-run

- **GIVEN** every outcome is `Skipped` with code `already_imported`
- **WHEN** the response is returned
- **THEN** the part is `Skipped` with code `teams_skipped`

#### Scenario: Unreadable entry

- **GIVEN** a `teams2` entry with no name and 2 valid entries
- **WHEN** the part is imported
- **THEN** the 2 valid teams are imported, the unreadable entry has outcome `Failed` with code `invalid_team`, and the part is `Imported`
