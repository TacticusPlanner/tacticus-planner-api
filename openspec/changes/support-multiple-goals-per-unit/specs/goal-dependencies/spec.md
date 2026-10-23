## ADDED Requirements

### Requirement: A new goal may depend on the unit's existing in-flight goals

Each combined-creation spec SHALL accept an optional `dependsOnGoalIds` list of existing goal ids, in addition to `dependsOnIndex`. Every id SHALL name an Active or Paused goal of the caller's profile for the same entity as the request, read under the profile lock. Otherwise the request SHALL be rejected with 400 and nothing is created. The created goal's `dependsOn` SHALL be the union of the resolved `dependsOnIndex` goals and `dependsOnGoalIds`.

#### Scenario: Milestone depends on an existing Ascension goal

- **GIVEN** Ragnar has an in-flight Ascension goal G ending at `Legendary:RedFiveStars`
- **WHEN** a Rank Adamantine1 spec declares `dependsOnGoalIds: [G]`
- **THEN** the Rank goal is created with `dependsOn` containing G

#### Scenario: Invalid existing dependency

- **WHEN** a spec's `dependsOnGoalIds` names a Completed goal, another unit's goal, or an id of another profile
- **THEN** the request is rejected with 400 and no goal is created

### Requirement: An existing Ascension dependency lifts the cap floor

Target cap validation (rarity-derived Rank and Ability caps) SHALL evaluate against the highest of: the unit's live progression, the end of every Ascension spec in the spec's in-request dependency closure, and the end of every existing Ascension goal named in `dependsOnGoalIds` or in those goals' own `dependsOn`.

#### Scenario: Ability target reachable through an existing Ascension goal

- **GIVEN** an Epic Character (ability cap 35) with an in-flight Ascension goal G ending at a Legendary progression (ability cap 50)
- **WHEN** an Ability spec targeting 50 declares `dependsOnGoalIds: [G]`
- **THEN** it passes cap validation

#### Scenario: Without the dependency the cap applies

- **GIVEN** the same unit and goal G
- **WHEN** an Ability spec targeting 50 declares no dependency on G
- **THEN** it is rejected for exceeding the current rarity cap of 35
