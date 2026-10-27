## MODIFIED Requirements

### Requirement: Teams belong to one lane of the plan

A team SHALL belong to exactly one lane (`alpha`, `beta` or `gamma`) of its plan and SHALL carry a trimmed `name` of 1–60 characters, an ordered list of 3–5 member unit ids, at most one reserve unit id, a set of covered objective indexes, at most one clear depth per run 1–3 (`expectedBattleClears` with its `expectedBattleClearsSource`, `estimate` or `manual`, and the time it was last written), and a `sortOrder` that is dense and 0-based within its lane. No unit SHALL appear twice in one team (members and reserve together). A team MAY cover zero objectives. A stored team with fewer than three members SHALL stay readable, deletable and in its lane order, but any create or update that leaves a team below three members SHALL be rejected.

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

- **GIVEN** a stored alpha team with members `[u1, u2]`
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
