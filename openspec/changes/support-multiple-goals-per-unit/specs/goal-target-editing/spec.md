## MODIFIED Requirements

### Requirement: Edited targets receive creation-equivalent validation and slot checks

The new end target SHALL pass the same catalog range, entity/type, target-shape, and dependency-cap rules as creation, evaluated against the goal's persisted baseline and current catalog. A target that is already reached in synced player data SHALL be saved when valid relative to that baseline and current catalog, subject to the same explicit revision and slot-conflict rules as any other valid target; it SHALL have zero remaining need but SHALL not silently change stored lifecycle status. Whenever an edit changes the goal's target key (`goal-target-milestones`), the new key SHALL be checked against the profile's other in-flight goals of the same unit and type; an occupied target SHALL return 409 `slotOccupied` naming the existing goal. Validation or conflict failure SHALL be atomic.

#### Scenario: Edit to an already reached target

- **GIVEN** Bellator has progressed beyond Silver3 since a Gold1 goal was created
- **WHEN** its owner edits the end target down to valid Silver3
- **THEN** the edit succeeds, no farming need remains, and the stored status is unchanged until a separate lifecycle action

#### Scenario: Rank target conflicts in one project

- **GIVEN** the profile has another in-flight Bellator Rank goal at the proposed target
- **WHEN** the target edit is submitted
- **THEN** no config changes and the 409 identifies the existing goal and the target

#### Scenario: Ascension target edit collides with a sibling milestone

- **GIVEN** Ragnar has in-flight Ascension goals ending at `Legendary:RedFiveStars` and `Mythic:OneBlueStar`
- **WHEN** the owner edits the Legendary goal's end to `Mythic:OneBlueStar`
- **THEN** no config changes and the 409 identifies the Mythic goal and its target
