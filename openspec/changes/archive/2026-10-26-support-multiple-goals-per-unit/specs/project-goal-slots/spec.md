## MODIFIED Requirements

### Requirement: In-flight uniqueness is scoped to project

For each profile, at most one Active/Paused goal SHALL occupy `(entityType, entityId, goalType, targetKey)`, where `targetKey` is the goal's canonical end-target key defined by `goal-target-milestones` (Rank: `rank-milestones`' normalized end target). Distinct target keys for one unit and goal type MAY coexist. Unlock has no target key: at most one Active/Paused Unlock goal SHALL occupy `(entityType, entityId, Unlock)`. Completed/Archived goals SHALL not occupy slots. Project membership SHALL NOT affect slot occupancy: one in-flight goal MAY belong to any number of projects, and two projects SHALL NOT carry separate in-flight instances of the same tuple/target.

#### Scenario: Different projects accept different instances

- **GIVEN** an Active Ragnar Rank goal targeting Gold1 belongs to project A
- **WHEN** the owner wants Ragnar Gold1 in project B
- **THEN** the existing goal is added to B; creating a second Ragnar Rank Gold1 goal is rejected and identifies the existing goal

#### Scenario: Same project rejects duplicate

- **GIVEN** an Active or Paused Ragnar Rank goal targeting Gold1 in any project
- **WHEN** another in-flight Ragnar Rank goal with the same normalized Gold1 target is created, in any project set
- **THEN** the operation is rejected and identifies the existing goal

#### Scenario: Historical goal frees slot

- **GIVEN** the profile has only Completed/Archived Ragnar Rank goals targeting Gold1
- **WHEN** a new Ragnar Rank Gold1 goal is created
- **THEN** creation succeeds

#### Scenario: Same project permits different Rank targets

- **GIVEN** an Active Ragnar Rank goal targeting Silver3
- **WHEN** another targeting Gold1 is created
- **THEN** both are accepted

#### Scenario: Distinct Ascension targets coexist

- **GIVEN** an Active Ragnar Ascension goal ending at `Legendary:RedFiveStars`
- **WHEN** another Ragnar Ascension goal ending at `Mythic:OneBlueStar` is created
- **THEN** both are accepted

#### Scenario: Equal Ascension target is a duplicate

- **GIVEN** an Active Ragnar Ascension goal ending at `Legendary:RedFiveStars` that started at `Epic:RedThreeStars`
- **WHEN** another Ragnar Ascension goal ending at `Legendary:RedFiveStars` is created, whatever its start
- **THEN** it is rejected with `slotOccupied` identifying the existing goal

#### Scenario: Unlock stays one per unit

- **GIVEN** an Active Unlock goal for a character
- **WHEN** another Unlock goal for that character is created
- **THEN** it is rejected with `slotOccupied`

### Requirement: Conflicts identify affected projects and goals

A slot conflict SHALL produce the same 409 in the unified error shape with `issueCode` `slotOccupied`, naming the entity type/id, goal type, normalized end target (`normalizedTarget`, the existing goal's target key; null only for Unlock) and the existing goal id, and carrying the existing goal under `current`.

#### Scenario: Multi-project create partially conflicts

- **GIVEN** the profile already has an in-flight Ragnar Rank Gold1 goal
- **WHEN** one combined request creates Ragnar Unlock, Ascension and Rank Gold1
- **THEN** the request is rejected atomically with `slotOccupied` identifying the existing Rank goal, and no goal is created

#### Scenario: Ability conflict names the target

- **GIVEN** an in-flight Calgar Ability goal targeting active 50 and passive 50
- **WHEN** another Calgar Ability goal targeting active 50 and passive 50 is created
- **THEN** the 409 carries `normalizedTarget` `"50:50"` and the existing goal id
