# personal-teams Specification

## Purpose
Defines the reusable teams a player keeps on their profile: the team record, its endpoints and order, and the rules each game mode applies to a lineup.

## Requirements

### Requirement: A team is a reusable lineup owned by one profile

A team SHALL belong to exactly one profile and SHALL be readable and writable only by that profile. `GET /api/v1/teams` SHALL return `{ teams }` in the profile's order. Each team SHALL carry exactly:

- `id` (string): a stable UUID that never changes, including on rename
- `revision` (integer)
- `name` (string)
- `position` (integer): 1-based and contiguous across the profile's teams
- `modes` (string array): any of `warOffense`, `warDefense`, `guildRaid`, `tournamentArena`, `horde`, `campaign`, `incursion`
- `characterIds` (string array): catalog character ids, in lineup order
- `flexStartIndex` (integer or null): the index of the first flex character in `characterIds`; null when the team has no flex characters
- `mowIds` (string array): catalog MoW ids, core MoWs first
- `mowFlexStartIndex` (integer or null): the index of the first flex MoW in `mowIds`; null when every MoW is core, and 0 when every MoW is flex
- `campaignGroupId` (string or null): a catalog campaign group id
- `incursionMowIds` (string array): the catalog MoW ids of the Incursions the team is built for
- `notes` (string or null)
- `rarityCap` (string or null): the rarity the team is prepared for, one of `Common`, `Uncommon`, `Rare`, `Epic`, `Legendary` and `Mythic`; null when it has none
- `issues` (rule code array): the rule codes the team currently breaks

The server SHALL send ids and codes only, never display names or icons. The rule codes SHALL be one closed enum in the API contract, used by `issues` and by the codes of a refused save, so a client generated from the contract knows every code the server can send. The V1 import key (see `v1-team-import`) SHALL NOT be sent. The rarity cap SHALL be stored and returned as sent, and SHALL NOT affect any team rule: it only tells clients what to preview the lineup at.

#### Scenario: Rule codes are in the contract

- **WHEN** the OpenAPI document is generated
- **THEN** `issues` is an array of an enum listing every code in the rules table, and nothing else

#### Scenario: Another profile's team

- **GIVEN** a team owned by another profile
- **WHEN** the caller updates or deletes it
- **THEN** the API responds `404`

#### Scenario: A saved rarity cap is returned

- **GIVEN** a team saved with `rarityCap` `Epic`
- **WHEN** teams are read
- **THEN** that team has `rarityCap` `Epic`, and its `issues` are the same as without a cap

#### Scenario: Rename keeps the id

- **WHEN** a team is renamed
- **THEN** its `id` is unchanged

### Requirement: Teams are created, updated and deleted through the teams endpoints

`POST /api/v1/teams` SHALL create a team at the end of the order. `PUT /api/v1/teams/{teamId}` SHALL replace a team's fields when the request's `revision` matches, and otherwise SHALL respond `409` with code `teamRevisionStale` and the current team. `DELETE /api/v1/teams/{teamId}` SHALL delete the team and close the gap in the remaining positions. A created or updated team SHALL be returned as `GET` returns it.

#### Scenario: Stale update

- **GIVEN** a team at revision 3
- **WHEN** a client updates it with revision 2
- **THEN** the API responds `409` with code `teamRevisionStale` and the team at revision 3, and nothing changes

#### Scenario: Delete closes the gap

- **GIVEN** teams at positions 1, 2 and 3
- **WHEN** the team at position 2 is deleted
- **THEN** the remaining teams are at positions 1 and 2, in their previous relative order

### Requirement: Teams are reordered as a whole list

`PUT /api/v1/teams/order` SHALL take `teamIds`, the profile's complete team list in its new order, and SHALL respond with the reordered teams. When `teamIds` is not exactly the profile's current set of teams, it SHALL respond `409` with code `teamOrderStale` and the current teams, and SHALL change nothing. A reorder SHALL NOT change a team's other fields.

#### Scenario: A team was added on another device

- **GIVEN** a client's list is missing a team created elsewhere
- **WHEN** it submits the new order
- **THEN** the API responds `409` with code `teamOrderStale` and the current teams

### Requirement: Names, notes and team count are limited

A team name SHALL be trimmed and SHALL be 3 to 40 characters. It SHALL be unique within the profile, ignoring case, and a duplicate SHALL be refused with `400` and code `teamNameTaken`. Notes SHALL be at most 300 characters, and blank notes SHALL be stored as null. A profile SHALL have at most 50 teams, and creating a 51st SHALL be refused with `400` and code `teamLimitReached`. The limit SHALL be a single configured value, 50 by default, read by creates and the import alike. The limit SHALL hold under concurrent creates and imports.

#### Scenario: Same name in another case

- **GIVEN** a team named "Mortarion core"
- **WHEN** another team is saved as "mortarion CORE"
- **THEN** the save is refused with `teamNameTaken`

#### Scenario: Fifty-first team

- **GIVEN** a profile with 50 teams
- **WHEN** a client creates a team
- **THEN** the API responds `400` with code `teamLimitReached`

### Requirement: Every save satisfies the team rules

Every create and update SHALL satisfy the rules below and SHALL otherwise be refused with `400` naming each broken rule by its code. The refusal SHALL carry `issueCode` `teamRulesBroken` and the codes, in the table's order, in a `rules` array typed by the contract's `TeamRuleCode` enum; its `errors` SHALL hold only a general message under `rules`, so clients read the codes from `rules`. Locked and unowned units SHALL be allowed: the rules do not read the player's roster. A unit SHALL appear at most once in `characterIds`, and `flexStartIndex`, when present, SHALL be between 1 and the last index of `characterIds`, because a team keeps at least one core character. A MoW SHALL appear at most once in `mowIds`, and `mowFlexStartIndex`, when present, SHALL be between 0 and the last index of `mowIds`: every MoW may be flex. A `rarityCap` outside the six rarities SHALL be refused as invalid input.

| Code | The rule |
|---|---|
| `nameLength` | The name is 3 to 40 characters |
| `noMode` | At least one mode is selected |
| `tooFewCharacters` | The team has at least 3 characters, in every mode; MoWs do not count |
| `unknownUnit` | Every character and MoW id exists in the catalog |
| `modeNotCombinable` | `campaign` and `incursion` are each the team's only mode |
| `tooManyCoreCharacters` | A team with `warOffense`, `warDefense` or `tournamentArena` has at most 5 core characters |
| `tooManyCoreMows` | A team with `warOffense`, `warDefense` or `tournamentArena` has at most 1 core MoW; flex MoWs do not count |
| `mowsNotAllowed` | A `campaign` or `incursion` team has no `mowIds` |
| `campaignRequired` | A `campaign` team has a `campaignGroupId`, and any other team has none |
| `unknownCampaign` | The `campaignGroupId` is an existing Standard, Mirror or event campaign group, not an Elite one |
| `coreCharacterMissing` | A `campaign` team has every core character of its campaign as a core character of the team |
| `factionNotAllowed` | Every character of a `campaign` team is from a faction its campaign allows, and every character of an `incursion` team is from a faction every one of its Incursions allows |
| `incursionRequired` | An `incursion` team has at least one `incursionMowIds` entry, and any other team has none |
| `unknownIncursion` | Every `incursionMowIds` entry has a served Incursion |
| `noSharedIncursionFaction` | The team's Incursions have at least one allowed faction in common |

Assumptions: a mode's limits are V1's, plus this change's stricter `tooFewCharacters`, `tooManyCoreMows`, `campaignRequired`, `coreCharacterMissing` and `incursionRequired`. A campaign's allowed factions and core characters are the served group's, and an Incursion's are the `allowedFactions` of its `incursions` record. The record's `minUnitsOfGrandAllianceNeeded` SHALL NOT be enforced, because V1 does not read it and its in-game meaning has not been verified.

#### Scenario: Six core characters in Guild War

- **GIVEN** a `warOffense` team with 6 characters and a null `flexStartIndex`
- **WHEN** it is saved
- **THEN** the API responds `400` naming `tooManyCoreCharacters`

#### Scenario: Flex characters do not count toward five

- **GIVEN** a `warDefense` team with 7 characters and `flexStartIndex` 5
- **WHEN** it is saved
- **THEN** it is saved

#### Scenario: Flex MoWs do not count toward one

- **GIVEN** a `warOffense` team with 3 MoWs and `mowFlexStartIndex` 1
- **WHEN** it is saved
- **THEN** it is saved

#### Scenario: Two core MoWs in Tournament Arena

- **GIVEN** a `tournamentArena` team with 2 MoWs and a null `mowFlexStartIndex`
- **WHEN** it is saved
- **THEN** the API responds `400` naming `tooManyCoreMows`

#### Scenario: Every MoW flex

- **GIVEN** a `warDefense` team with 2 MoWs and `mowFlexStartIndex` 0
- **WHEN** it is saved
- **THEN** it is saved

#### Scenario: Guild Raid has no size limit

- **GIVEN** a `guildRaid` team with 7 core characters and 2 MoWs
- **WHEN** it is saved
- **THEN** it is saved

#### Scenario: Campaign combined with another mode

- **GIVEN** a team with modes `campaign` and `horde`
- **WHEN** it is saved
- **THEN** the API responds `400` naming `modeNotCombinable`

#### Scenario: Campaign core characters

- **GIVEN** a `campaign` team on `campaign1`, whose core characters are `ultraTigurius`, `ultraEliminatorSgt` and `ultraInceptorSgt`, with `ultraInceptorSgt` missing
- **WHEN** it is saved
- **THEN** the API responds `400` naming `coreCharacterMissing`

#### Scenario: Core character listed as flex

- **GIVEN** a `campaign` team on `campaign1` whose `flexStartIndex` places `ultraTigurius` in the flex section
- **WHEN** it is saved
- **THEN** the API responds `400` naming `coreCharacterMissing`

#### Scenario: Elite is not a separate campaign

- **WHEN** a `campaign` team is saved with `campaignGroupId` `elite1`
- **THEN** the API responds `400` naming `unknownCampaign`

#### Scenario: Incursion faction is outside what the MoW allows

- **GIVEN** an `incursion` team for `orksRukkatrukk`, whose Incursion allows Ultramarines, BlackTemplars, SpaceWolves, DarkAngels, BloodAngels and Custodes
- **WHEN** it is saved with a Sisterhood character
- **THEN** the API responds `400` naming `factionNotAllowed`

#### Scenario: A team for two Incursions

- **GIVEN** an `incursion` team for `tauBroadside`, which allows Sisterhood, and `orksRukkatrukk`, which does not
- **WHEN** it is saved with a Sisterhood character
- **THEN** the API responds `400` naming `factionNotAllowed`, because the character cannot enter both Incursions

#### Scenario: Incursions with no faction in common

- **GIVEN** an `incursion` team for `ultraDreadnought`, which allows Chaos factions, and `tauBroadside`, which allows Imperial factions
- **WHEN** it is saved
- **THEN** the API responds `400` naming `noSharedIncursionFaction`

### Requirement: A team's issues are reported on every read

`issues` SHALL be derived from the current catalog on every read, using the rule codes above, and SHALL be empty for a team that satisfies every rule. A team with issues SHALL remain readable, reorderable and deletable. Updating it SHALL require the updated team to satisfy every rule. The API SHALL NOT repair a team on its own.

#### Scenario: Imported team that breaks a rule

- **GIVEN** a team imported with modes `warOffense` and 6 core characters
- **WHEN** teams are read
- **THEN** that team has `issues` `["tooManyCoreCharacters"]`

#### Scenario: A rename cannot skip an issue

- **GIVEN** a team with issue `tooManyCoreCharacters`
- **WHEN** the client updates only its name
- **THEN** the API responds `400` naming `tooManyCoreCharacters`, and the name is unchanged

#### Scenario: Catalog no longer has a unit

- **GIVEN** a saved team whose unit id is no longer in the catalog
- **WHEN** teams are read
- **THEN** the team is returned with issue `unknownUnit`

### Requirement: Teams follow the profile's lifecycle

A profile's teams SHALL be deleted with the profile. The staging data transfer SHALL export a profile's teams and SHALL replace the target profile's teams on import, keeping their order and V1 import keys and assigning new ids. Teams SHALL NOT be shared with any other profile or guild.

#### Scenario: Account purge

- **WHEN** a profile is purged
- **THEN** its teams no longer exist

#### Scenario: Staging transfer

- **GIVEN** a production profile with 3 ordered teams
- **WHEN** its bundle is imported on staging
- **THEN** the staging profile has exactly those 3 teams in the same order, with new ids and the same V1 import keys
