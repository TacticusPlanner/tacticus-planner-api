## ADDED Requirements

### Requirement: Every goal type except Unlock has a canonical end-target key

Each goal SHALL have a target key derived only from its end state, computed by one shared function used by slot checks, target edits, conflict responses and V1 import. Start/baseline, farming strategy, farming locations, acquisition sources and notes SHALL NOT be part of it. Unlock SHALL have no key.

#### Scenario: Ascension start does not distinguish

- **WHEN** two Ascension goals for one unit both end at `Legendary:RedFiveStars`, one starting at `Epic:RedThreeStars` and one at `Legendary:RedThreeStars`
- **THEN** their target keys are equal

#### Scenario: Unadvanced ability track does not distinguish

- **GIVEN** a Character Ability goal created at active 40/passive 40 targeting active 50 and leaving passive at 40
- **WHEN** another is created later at passive 42, targeting active 50 and leaving passive at 42
- **THEN** both keys are `"50:-"` and the second is a duplicate

#### Scenario: Upgrade target order does not distinguish

- **WHEN** two Upgrade goals for one character carry `[upgA×3, upgB×2]` and `[upgB×2, upgA×3]` with the same `rankRange`
- **THEN** their keys are equal

#### Scenario: Upgrade quantity distinguishes

- **WHEN** two Upgrade goals for one character carry `upgA×3` and `upgA×5` with no range
- **THEN** their keys differ and both may be in flight

### Requirement: Target keys are defined per goal type

The key SHALL be: for Rank, the `rank-milestones` normalized end target; for Ascension, the end progression; for Ability (Character and Machine of War), the pair (active end, passive end), with a track whose end is not above its start written as not advanced (`-`); for Upgrade, the unordered set of (upgrade id, quantity) targets together with each present range group and its bounds.

#### Scenario: Ability key

- **WHEN** a Character Ability goal targets active 50 and passive 45
- **THEN** its key is `"50:45"`

#### Scenario: Rank key is unchanged

- **WHEN** a Rank goal targets Silver3 with point-five
- **THEN** its key equals the `rank-milestones` normalized key for Silver3 with three applied slots

### Requirement: Distinct-target goals are independently managed

Goals of one unit and type with distinct target keys SHALL each keep their own id, status, priority, dependencies, notes and project memberships. Each SHALL be independently reorderable, pausable, resumable, editable and deletable. Deleting or completing one SHALL free only its own target.

#### Scenario: Two Ascension milestones

- **GIVEN** Ragnar has Ascension goals ending at `Legendary:RedFiveStars` (priority 3) and `Mythic:OneBlueStar` (priority 9)
- **WHEN** the owner pauses the Mythic goal
- **THEN** only the Mythic goal is Paused and the Legendary goal keeps priority 3 and status Active

### Requirement: A combined request may create several goals of one type

A combined creation request SHALL accept more than one spec of the same goal type for its unit when their target keys differ. The specs SHALL be checked against the account's in-flight goals and against each other. Two specs with an equal target key SHALL reject the whole request with `slotOccupied`, naming the earlier spec's goal. Created goals SHALL take consecutive priorities in request order.

#### Scenario: Rank ladder in one request

- **WHEN** one combined request for Ragnar carries Rank specs ending at Gold1, Diamond1, Diamond3 and Adamantine1, each depending on the previous spec by `dependsOnIndex`
- **THEN** four Rank goals are created atomically with consecutive priorities and the declared dependency chain

#### Scenario: Equal specs in one request

- **WHEN** one combined request carries two Ascension specs ending at the same progression
- **THEN** the request is rejected with `slotOccupied` and no goal is created

#### Scenario: Unlock remains single in a request

- **WHEN** one combined request carries two Unlock specs
- **THEN** the request is rejected with `slotOccupied`
