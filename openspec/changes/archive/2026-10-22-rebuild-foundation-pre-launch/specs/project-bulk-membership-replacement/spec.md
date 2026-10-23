## MODIFIED Requirements

### Requirement: Batch validation is atomic and actionable
The operation SHALL reject an unknown/unowned goal or a removal that would leave a goal with no project, without applying any part of the desired set. Membership replacement SHALL NOT perform slot checks: in-flight uniqueness is a per-profile property of the goal, so adding an existing goal to a project can never create a duplicate. Last-membership rejection SHALL identify the blocked goal IDs for client review in the unified error shape.

#### Scenario: One blocked removal among valid additions
- **WHEN** a desired set adds two valid goals but removes a third goal from its only project
- **THEN** no change commits and the response identifies that blocked goal

#### Scenario: Occupied slot
- **GIVEN** the profile holds an in-flight Ragnar Rank Silver3 goal and an in-flight Ragnar Rank Gold1 goal
- **WHEN** a desired set includes both in one project
- **THEN** the replacement commits; both are members
