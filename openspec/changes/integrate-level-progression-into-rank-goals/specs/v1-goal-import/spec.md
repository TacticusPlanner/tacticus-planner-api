## MODIFIED Requirements

### Requirement: Missing prerequisites are created automatically by the same rules as manual creation

When automatic prerequisite creation is selected, the import SHALL create the prerequisite goals a manual creation of the same target would suggest, using the same rules and minimum targets:

- an **Unlock** goal when the unit is absent from the roster and the imported goal requires it;
- an **Ascension** goal when a target is above the progression-derived cap, targeting the lowest progression satisfying every such target for the unit;
- a **Level** goal for a Character Ability target that needs a level above current and is not already covered by an independently required Level goal. A Rank target's routine level progression SHALL be integral to Rank and SHALL NOT by itself synthesize a Level goal.

Each created prerequisite SHALL be declared as a dependency of every imported goal that requires it and placed immediately before them in the constructed import sequence. Import itself owns this placement; no automatic within-unit reorder is relied upon. A prerequisite SHALL NOT be created when imported goals or the account already contain one of its type for the unit; the shortfall SHALL be reported and the existing target SHALL not be altered. Synthesized Unlock/Level targets SHALL pass manual-creation validation; on validation failure the prerequisite SHALL not be created, the failure SHALL be reported, and affected goals proceed without that dependency edge. Each created prerequisite SHALL have an outcome marked automatically added and naming the source goal it unblocks.

#### Scenario: Unlock is created for a unit not in the roster

- **GIVEN** a V1 Rank goal for a character absent from the account's roster
- **WHEN** goals are imported with automatic prerequisites selected
- **THEN** an Unlock goal is created as a Rank dependency and reported as automatically added

#### Scenario: Ascension is created at the minimum satisfying target

- **GIVEN** a V1 Rank target above the progression-derived cap
- **WHEN** goals are imported with automatic prerequisites selected
- **THEN** Ascension targets the lowest progression making Rank reachable and Rank depends on it

#### Scenario: One prerequisite covers several imported goals

- **GIVEN** V1 Rank and Ability goals for one character both need higher progression
- **WHEN** goals are imported with automatic prerequisites selected
- **THEN** one Ascension goal covers both and both depend on it

#### Scenario: An imported Ascension goal suppresses synthesis and is not raised

- **GIVEN** an imported Ascension target below what an imported Rank goal needs
- **WHEN** goals are imported with automatic prerequisites selected
- **THEN** no additional Ascension is created, its target is unchanged, and a shortfall outcome is reported

#### Scenario: Prerequisites are not created when not selected

- **GIVEN** a V1 Rank goal for an absent character
- **WHEN** goals are imported without automatic prerequisites selected
- **THEN** only the Rank goal is created and no Unlock goal is added

#### Scenario: A requirement already met needs no prerequisite

- **GIVEN** a V1 Rank target within the character's progression cap, with routine leveling below target
- **WHEN** goals are imported with automatic prerequisites selected
- **THEN** no Ascension or Level goal is created solely for that Rank goal

#### Scenario: A created prerequisite precedes its dependents in the import sequence

- **GIVEN** an imported Ability goal requires a Level prerequisite the account lacks
- **WHEN** that prerequisite is created automatically
- **THEN** the Level goal's project priority precedes the Ability goal's priority

#### Scenario: Rank import does not synthesize Level

- **GIVEN** an imported Rank target requires a higher character level and no Ability target requires that level
- **WHEN** goals are imported with automatic prerequisites selected
- **THEN** no separate Level goal is created and Rank's target still carries the level requirement
