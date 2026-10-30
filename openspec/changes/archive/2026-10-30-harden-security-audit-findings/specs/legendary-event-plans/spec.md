## ADDED Requirements

### Requirement: A lane holds at most 50 teams

A lane of a plan SHALL hold at most 50 teams. A `POST …/teams` request on a lane that already holds 50 SHALL be a 400 with `issueCode` `legendaryEventTeamLimitReached` on `laneId`. The plan and its revision SHALL be unchanged. The check SHALL run against the loaded plan, inside the same write that checks `expectedRevision`.

#### Scenario: Lane full

- **GIVEN** a plan whose alpha lane holds 50 teams
- **WHEN** the caller creates another team on alpha
- **THEN** the response is 400 `legendaryEventTeamLimitReached` and the plan revision is unchanged

#### Scenario: Other lanes are independent

- **GIVEN** a plan whose alpha lane holds 50 teams and whose beta lane holds 3
- **WHEN** the caller creates a team on beta
- **THEN** the team is created with `sortOrder` 3
