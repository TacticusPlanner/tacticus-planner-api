## ADDED Requirements

### Requirement: A profile holds at most 200 non-archived goals per goal type

A profile SHALL hold at most 200 goals of one goal type outside Archived. Active, Paused and Completed goals count; Archived goals do not. A `POST /goals` that would put a type over the cap SHALL be a 400 with `issueCode` `goalLimitReached` on `goals`, and SHALL create no goal. The count SHALL be read under the profile lock. The cap also bounds the unpaginated non-archived `GET /goals` list.

#### Scenario: Batch crosses the cap

- **GIVEN** a profile with 199 non-archived Rank goals
- **WHEN** it posts two new Rank goals in one request
- **THEN** the response is 400 `goalLimitReached` and neither goal is created

#### Scenario: Archived goals do not count

- **GIVEN** a profile with 200 Rank goals, 10 of them Archived
- **WHEN** it posts one new Rank goal
- **THEN** the goal is created

### Requirement: Snapshot resource lists are bounded

On each goal in a `POST /goals` request, `snapshot.initialRequirement` and `snapshot.initialInventoryContribution` SHALL each hold at most 250 entries. Their resource ids SHALL be distinct after trimming, at most 128 characters long, and each a Game Catalog upgrade material id. A violation SHALL be a 400 that creates nothing.

#### Scenario: Snapshot resource that is not an upgrade material

- **WHEN** a goal's `initialRequirement` names a resource id absent from the catalog's upgrades
- **THEN** the response is 400 and no goal is created

#### Scenario: Repeated snapshot resource id

- **WHEN** a goal's `initialRequirement` lists the same resource id twice
- **THEN** the response is 400 `validation` and no goal is created

### Requirement: Upgrade targets and dependency indexes are bounded

On each goal in a `POST /goals` request:

- `config.upgrade` SHALL be present only on an Upgrade goal, and SHALL hold at most 250 targets;
- `dependsOnIndex` entries SHALL be distinct.

A violation SHALL be a 400 that creates nothing.

#### Scenario: Upgrade target on a Rank goal

- **WHEN** a Rank goal carries `config.upgrade`
- **THEN** the response is 400 and no goal is created

#### Scenario: Repeated dependency index

- **WHEN** a goal's `dependsOnIndex` is `[0, 0]`
- **THEN** the response is 400 `validation` and no goal is created
