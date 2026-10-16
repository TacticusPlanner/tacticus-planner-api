# Spec Delta

## MODIFIED Requirements

### Requirement: Teams belong to one lane of the plan

A team SHALL belong to exactly one lane (`alpha`, `beta` or `gamma`) of its plan and SHALL carry a trimmed `name` of 1–60 characters, an ordered list of 3–5 member unit ids, at most one reserve unit id, a set of covered objective indexes, at most one clear depth per run 1–3 (`expectedBattleClears` with its `expectedBattleClearsSource`, `estimate` or `manual`, and the time it was last written), and a `sortOrder` that is dense and 0-based within its lane. No unit SHALL appear twice in one team (members and reserve together). A team MAY cover zero objectives. A team stored with fewer than three members (written before this rule) SHALL stay readable, deletable and in its lane order, but any create or update that leaves a team below three members SHALL be rejected.

#### Scenario: Create a team

- **GIVEN** a plan at revision 1 for an event whose alpha lane allows units `u1`…`u6` and has objectives with indexes 0–4
- **WHEN** the caller sends `POST …/teams` with `expectedRevision` 1, `laneId` `alpha`, `name` "Melee", `memberUnitIds` `[u1,u2,u3]`, `reserveUnitId` `u4`, `objectiveIndexes` `[0,3]`, `run` 1, `expectedBattleClears` 7 and `expectedBattleClearsSource` `manual`
- **THEN** the response is 200 with the whole plan at revision 2, containing the new team with a server-generated id, `sortOrder` 0, the given fields and `runDepths` `[{run 1, 7, manual}]`

#### Scenario: A team is appended after the lane's existing teams

- **GIVEN** the alpha lane already has teams at `sortOrder` 0 and 1
- **WHEN** a team is created on alpha
- **THEN** it has `sortOrder` 2 and the existing orders are unchanged

#### Scenario: Update a team

- **GIVEN** a team on alpha at plan revision 2
- **WHEN** the caller sends `PUT …/teams/{teamId}` with `expectedRevision` 2 and new members, objectives, name and a depth for `run` 1
- **THEN** the team's lane and `sortOrder` are unchanged, the other fields are replaced, and the response is the plan at revision 3

#### Scenario: A depth write touches only its run

- **GIVEN** a team with `runDepths` `[{run 1, 7, manual}]`
- **WHEN** the caller sends `PUT …/teams/{teamId}` with `run` 2, `expectedBattleClears` 9 and `expectedBattleClearsSource` `manual`
- **THEN** the team's `runDepths` is `[{run 1, 7, manual}, {run 2, 9, manual}]`

#### Scenario: A null depth clears only its run

- **GIVEN** a team with depths for runs 1 and 2
- **WHEN** the caller sends `PUT …/teams/{teamId}` with `run` 2 and `expectedBattleClears` null
- **THEN** the team's `runDepths` is `[{run 1, …}]`

#### Scenario: Lane cannot change on update

- **WHEN** a `PUT …/teams/{teamId}` body carries a `laneId`
- **THEN** the response is 400 naming `laneId`

#### Scenario: Delete a team re-densifies its lane

- **GIVEN** alpha teams A, B, C at orders 0, 1, 2
- **WHEN** the caller deletes B with `DELETE …/teams/{teamId}?expectedRevision=<current>`
- **THEN** A and C have orders 0 and 1 and the response is the plan at the next revision

#### Scenario: Unknown team id

- **WHEN** `PUT` or `DELETE …/teams/{teamId}` names a team that is not on this plan
- **THEN** the response is 404

#### Scenario: A short team written earlier stays readable

- **GIVEN** a stored alpha team with members `[u1, u2]` written before the minimum existed
- **WHEN** the plan is read, the team is reordered within alpha, or the team is deleted
- **THEN** each call succeeds and the team is returned, kept in the new order, or removed

#### Scenario: Updating a short team without completing it

- **GIVEN** the same stored team with members `[u1, u2]`
- **WHEN** the caller sends `PUT …/teams/{teamId}` with members `[u1, u2]` and a new name
- **THEN** the response is 400 naming `memberUnitIds` and the team is unchanged

#### Scenario: Completing a short team

- **GIVEN** the same stored team
- **WHEN** the caller sends `PUT …/teams/{teamId}` with members `[u1, u2, u3]`
- **THEN** the response is 200 and the team has three members

### Requirement: Every team write is validated against the current Game Catalog

On create and update the system SHALL reject, with 400 and the offending field name, a `laneId` outside `alpha|beta|gamma`; a team with fewer than three or more than five members (the reserve does not count); a member or reserve unit id not present in that lane's `availableUnitIds`; a repeated unit; an `objectiveIndexes` entry that is not an `index` of that lane's `unitsRestrictions`, or a repeated index; a `run` outside 1..3; an `expectedBattleClears` outside 1..(battle count of the lane); a non-null depth without a source, or a source without a depth; an empty or over-long name. The plan's `catalogVersion` SHALL be set to the current catalog version on every successful write.

#### Scenario: Unit not allowed on the lane

- **GIVEN** unit `u9` is not in the alpha lane's `availableUnitIds`
- **WHEN** a team is created on alpha with `u9` as a member
- **THEN** the response is 400 naming `memberUnitIds` and no team is created

#### Scenario: Objective index outside the lane

- **WHEN** a team on alpha carries `objectiveIndexes` `[5]` and the lane has indexes 0–4
- **THEN** the response is 400 naming `objectiveIndexes`

#### Scenario: Depth outside the lane

- **GIVEN** the lane has 18 battles
- **WHEN** a team carries `expectedBattleClears` 19
- **THEN** the response is 400 naming `expectedBattleClears`

#### Scenario: Run outside the event

- **WHEN** a team carries `run` 4
- **THEN** the response is 400 naming `run`

#### Scenario: Depth and source travel together

- **WHEN** a team carries `expectedBattleClears` 5 and `expectedBattleClearsSource` null
- **THEN** the response is 400 naming `expectedBattleClearsSource`

#### Scenario: Reads never fail on catalog drift

- **GIVEN** a plan written under an earlier catalog version whose objective indexes no longer validate
- **WHEN** the plan is read
- **THEN** the response is 200 with the stored ids and the stored `catalogVersion`

#### Scenario: Team below the minimum

- **WHEN** a team is created on alpha with `memberUnitIds` `[u1, u2]`
- **THEN** the response is 400 naming `memberUnitIds` and no team is created

#### Scenario: Reserve does not count toward the minimum

- **WHEN** a team is created with `memberUnitIds` `[u1, u2]` and `reserveUnitId` `u3`
- **THEN** the response is 400 naming `memberUnitIds`

#### Scenario: Team above the maximum

- **WHEN** a team is created with six members
- **THEN** the response is 400 naming `memberUnitIds`

#### Scenario: Exactly three and exactly five members are accepted

- **WHEN** teams are created with three members and with five members
- **THEN** both responses are 200
