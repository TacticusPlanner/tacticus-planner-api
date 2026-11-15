## MODIFIED Requirements

### Requirement: An existing Ascension dependency lifts the cap floor

Target cap validation (rarity-derived Rank and Ability caps) at creation SHALL evaluate against the highest of: the unit's live progression, the end of every Ascension spec in the spec's in-request dependency closure, and the end of every existing Ascension goal reachable from `dependsOnGoalIds` through existing `dependsOn` edges, at any depth. The traversal SHALL pass through completed goals (only the `dependsOnGoalIds` roots must be in flight) and SHALL visit each goal at most once. A target edit SHALL NOT apply the rarity-derived Ability cap (see `goal-target-editing`), so it needs no Ascension floor.

#### Scenario: Ability target reachable through an existing Ascension goal

- **GIVEN** an Epic Character (ability cap 35) with an in-flight Ascension goal G ending at a Legendary progression (ability cap 50)
- **WHEN** an Ability spec targeting 50 declares `dependsOnGoalIds: [G]`
- **THEN** it passes cap validation

#### Scenario: Ascension reached through a chain of existing goals

- **GIVEN** an Epic Character with an in-flight Ascension goal G ending at a Legendary progression, and in-flight Rank goals R2 depending on G and R3 depending on R2
- **WHEN** an Ability spec targeting 50 declares `dependsOnGoalIds: [R3]`
- **THEN** it passes cap validation, because G is reachable from R3

#### Scenario: Without the dependency the cap applies

- **GIVEN** the same unit and goal G
- **WHEN** an Ability spec targeting 50 declares no dependency on G
- **THEN** it is rejected for exceeding the current rarity cap of 35
