## Purpose

Defines how a V1 planner profile's goals are imported into V2: which V1 goals
become which V2 goals, how their targets and shard sources are derived, when a
goal is skipped rather than not imported, the order goals are created in,
automatic creation of missing prerequisites, and the per-goal outcome report
the caller receives.

## ADDED Requirements

### Requirement: Imported goals are created by the import operation

The import operation SHALL create the imported goals itself. It SHALL NOT
return goal-creation requests for the caller to submit. Goals SHALL be created
in the caller's default project, with the same in-flight status a goal created
through the ordinary create-goal operation would receive.

The import SHALL NOT require the caller to supply, or re-supply, anything
derived from the V1 profile: V1 credentials are used once, within the same
operation, and are not retained.

#### Scenario: Import creates goals in one operation

- **GIVEN** an account with player data and a V1 profile containing supported goals
- **WHEN** the goals part is imported
- **THEN** the goals exist on the account when the operation returns, and the response contains
  no goal-creation request for the caller to submit

#### Scenario: Import is repeatable without duplicating goals

- **GIVEN** an import has already created goals for an account
- **WHEN** the same V1 profile is imported again
- **THEN** no duplicate goals are created, and each already-present goal reports as skipped
  because it already exists

### Requirement: The goals part is refused when the account has no player data

Every goal's starting point, and every decision about whether a target is
already reached, SHALL be derived from the account's player data. When the
account has no player data recorded, the goals part SHALL be refused: no goals
SHALL be created, and the part SHALL report a single blocking outcome stating
that player data must be synced before goals can be imported.

The other selectable parts of the import SHALL be unaffected by this refusal.

#### Scenario: Goals are refused without player data

- **GIVEN** an account with no player data recorded
- **WHEN** the goals part is selected for import
- **THEN** no goals are created and the goals part reports that a player-data sync is required

#### Scenario: Other parts still import without player data

- **GIVEN** an account with no player data recorded
- **WHEN** the goals part and the personal API key part are both selected
- **THEN** the personal API key part is processed normally and only the goals part is refused

#### Scenario: Importing after a sync succeeds

- **GIVEN** the goals part was previously refused for want of player data
- **WHEN** player data has since been recorded and the goals part is imported again
- **THEN** goals are created

### Requirement: Every source V1 goal receives exactly one outcome

The response SHALL contain exactly one outcome entry per goal present in the
V1 profile, in V1 priority order. Each outcome SHALL carry a status of
created, skipped, or failed; a stable machine-readable code; a human-readable
message; the entity type and entity id it concerned when known; the V2 goal
type it concerned when one was determined; and the id of the V2 goal it
created or matched when one applies.

The number of created outcomes SHALL equal the number of V2 goals the import
created from source goals. No count in the response SHALL mix source goals with
any other unit of aggregation.

#### Scenario: Outcome count matches the source profile

- **GIVEN** a V1 profile containing 34 goals
- **WHEN** the goals part is imported
- **THEN** the response contains exactly 34 outcome entries

#### Scenario: Several goals for one unit are reported individually

- **GIVEN** a V1 profile containing a Rank goal and an Ascension goal for the same character
- **WHEN** both are imported successfully
- **THEN** two created outcomes are reported, each naming its own goal type and created goal id

#### Scenario: Outcomes follow V1 priority order

- **GIVEN** a V1 profile whose goals have distinct priorities
- **WHEN** the goals part is imported
- **THEN** the outcome entries appear in ascending V1 priority order

### Requirement: Skipped and not-imported outcomes are distinguishable

An outcome SHALL distinguish a goal that needed no import from a goal whose
information could not be carried across.

A goal SHALL report as skipped, with a code identifying which case applies,
when: its target is already reached according to the account's player data; the
account already has a goal of that type for that unit; or it was merged into
another source goal's target for the same unit and type.

A goal SHALL report as not imported, with a code identifying which case
applies, when: its V1 goal type is not supported; its unit is not present in
the game catalog; its target does not exist on the V2 progression ladder; or it
carries no usable target.

#### Scenario: Already-reached target is a skip

- **GIVEN** a V1 Rank goal whose target rank is at or below the character's current rank
- **WHEN** the goals part is imported
- **THEN** that goal reports as skipped with a code identifying an already-reached target

#### Scenario: Unsupported V1 goal type is not imported

- **GIVEN** a V1 goal of the upgrade-material or pre-farm-material type
- **WHEN** the goals part is imported
- **THEN** that goal reports as not imported with a code identifying an unsupported V1 goal
  type, and the message SHALL NOT promise future support

#### Scenario: Unknown unit is not imported and names the V1 unit

- **GIVEN** a V1 goal whose character does not match any unit in the game catalog
- **WHEN** the goals part is imported
- **THEN** that goal reports as not imported with a code identifying an unknown unit, and the
  outcome carries the unit identifier as it appeared in the V1 profile

#### Scenario: Already-existing goal is a skip that identifies the existing goal

- **GIVEN** the account already has an Ascension goal for a character
- **WHEN** a V1 Ascension goal for that character is imported
- **THEN** that goal reports as skipped because it already exists, and the outcome carries the
  existing goal's id

### Requirement: Duplicate V1 goals for one unit and type are merged and reported

When a V1 profile contains more than one goal of the same type for the same
unit, the import SHALL produce at most one V2 goal for that unit and type.

For Rank and Ascension goals the merged target SHALL span the duplicates: the
lowest starting point and the highest end point among them. For every other
goal type the highest-priority duplicate SHALL be used.

The surviving source goal SHALL report as created. Every merged-away source
goal SHALL report as skipped with a code identifying a merge, and SHALL carry
the id of the goal it was merged into.

#### Scenario: Two rank goals merge into one spanning goal

- **GIVEN** a V1 profile with two Rank goals for one character, targeting different ranks
- **WHEN** the goals part is imported
- **THEN** one Rank goal is created whose target is the higher of the two
- **AND** the other source goal reports as skipped with a merge code and the created goal's id

#### Scenario: Duplicate ability goals keep the higher-priority one

- **GIVEN** a V1 profile with two ability goals for one unit
- **WHEN** the goals part is imported
- **THEN** the higher-priority one reports as created and the other reports as skipped with a
  merge code

### Requirement: V1 shard-source choices are carried into acquisition sources

An imported Unlock or Ascension goal's acquisition sources SHALL be derived
from the corresponding V1 goal's shard-source fields. A V1 goal whose shard
farming was restricted to Onslaught SHALL import with an Onslaught acquisition
source and SHALL NOT import as unrestricted campaign farming. A V1 goal
combining Onslaught and energy farming SHALL import with both an Onslaught and
a Campaign source. A V1 goal whose campaign usage indicated no campaign
farming SHALL NOT import with a Campaign source on that account.

Where an acquisition source kind is not valid for the imported goal's entity
and goal type, it SHALL be omitted rather than causing the goal to fail, and
the outcome SHALL remain created.

#### Scenario: Onslaught-only ascension keeps its Onslaught source

- **GIVEN** a V1 Ascension goal whose shard farm type is Onslaught
- **WHEN** the goals part is imported
- **THEN** the created Ascension goal carries an Onslaught acquisition source and no Campaign
  source

#### Scenario: Combined farming imports both sources

- **GIVEN** a V1 Ascension goal whose shard farm type covers both Onslaught and energy
- **WHEN** the goals part is imported
- **THEN** the created goal carries both an Onslaught and a Campaign acquisition source

#### Scenario: Machine-of-War goal omits an invalid source kind

- **GIVEN** a V1 goal for a Machine of War carrying a shard-source choice that is not valid for
  Machine-of-War goals
- **WHEN** the goals part is imported
- **THEN** the goal is created without that source kind and reports as created

### Requirement: V1 goal notes are preserved

An imported goal SHALL carry the notes from its source V1 goal. When several
source goals merge into one V2 goal, the merged goal SHALL carry the notes of
every contributing source goal that had any.

#### Scenario: Notes survive the import

- **GIVEN** a V1 goal with notes
- **WHEN** it is imported
- **THEN** the created goal carries those notes

#### Scenario: Merged goals combine their notes

- **GIVEN** two merging V1 Rank goals for one character, both with notes
- **WHEN** they are imported
- **THEN** the created goal's notes contain both source goals' notes

### Requirement: Imported goals preserve V1 unit order

Goals SHALL be created in ascending V1 priority order. The resulting project
order SHALL place unit blocks in the order those units first appear in the V1
priority sequence, and within a unit SHALL place a goal's prerequisites before
it.

Absolute V1 priority numbers SHALL NOT be preserved, and V1's interleaving of
different units' goals SHALL NOT be preserved, because a project orders whole
unit blocks. The outcome report SHALL NOT claim an ordering guarantee stronger
than unit order.

#### Scenario: Unit order follows V1 priority

- **GIVEN** a V1 profile whose lowest-priority goals belong to character A and whose next
  belong to character B
- **WHEN** the goals part is imported
- **THEN** A's unit block precedes B's unit block in the project's goal order

#### Scenario: A unit's goals are contiguous

- **GIVEN** a V1 profile that interleaves goals for two characters
- **WHEN** the goals part is imported
- **THEN** each character's goals are contiguous in the project's goal order

### Requirement: Missing prerequisites are created automatically by the same rules as manual creation

When automatic prerequisite creation is selected, the import SHALL create the
prerequisite goals a manual creation of the same target would suggest, using
the same rules and the same minimum targets:

- an **Unlock** goal when the unit is absent from the account's roster and the
  imported goal requires the unit to exist;
- an **Ascension** goal when an imported target is above the unit's
  progression-derived cap, targeting the lowest progression that satisfies
  every such target for that unit;
- a **Level** goal, for characters only, when an imported target requires a
  character level above the unit's current level, targeting the lowest level
  that satisfies every such target for that unit.

Each created prerequisite SHALL be declared as a dependency of every imported
goal that required it, and SHALL be ordered before them within the unit.

A prerequisite SHALL NOT be created when the unit's imported goals already
include one of that type, or when the account already has one of that type for
that unit. In those cases the requirement SHALL be reported rather than
satisfied, and the existing goal's target SHALL NOT be altered.

Each created prerequisite SHALL be reported as its own outcome entry,
identified as automatically added and naming the source goal it unblocks.

#### Scenario: Unlock is created for a unit not in the roster

- **GIVEN** a V1 Rank goal for a character absent from the account's roster
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** an Unlock goal for that character is created, declared as a dependency of the Rank
  goal, and reported as an automatically added prerequisite

#### Scenario: Ascension is created at the minimum satisfying target

- **GIVEN** a V1 Rank goal whose target rank is above the character's progression-derived cap
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** an Ascension goal is created targeting the lowest progression that makes the rank
  target reachable, and the Rank goal declares a dependency on it

#### Scenario: One prerequisite covers several imported goals

- **GIVEN** a V1 profile with a Rank goal and an ability goal for one character, both above the
  character's progression-derived cap
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** a single Ascension goal is created satisfying both, and both goals declare a
  dependency on it

#### Scenario: An imported Ascension goal suppresses synthesis and is not raised

- **GIVEN** a V1 profile containing both a Rank goal needing a higher progression and an
  Ascension goal for the same character whose target is below what the Rank goal needs
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** no additional Ascension goal is created, the imported Ascension goal's target is
  unchanged, and an outcome reports that the existing target does not reach what the Rank goal
  requires

#### Scenario: Prerequisites are not created when not selected

- **GIVEN** a V1 Rank goal for a character absent from the account's roster
- **WHEN** the goals part is imported without automatic prerequisites selected
- **THEN** only the Rank goal is created and no Unlock goal is added

#### Scenario: A requirement already met needs no prerequisite

- **GIVEN** a V1 Rank goal whose target is within the character's progression-derived cap and
  current level
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** no Ascension or Level goal is created for that character

### Requirement: A failure for one goal does not discard the others

A source goal that cannot be created SHALL report as failed with a code and
message identifying why, and SHALL NOT prevent the remaining goals from being
created. The outcome report SHALL be sufficient to identify a failed goal
without reference to a server trace identifier.

#### Scenario: One rejected target does not lose the rest

- **GIVEN** a V1 profile in which one goal's target is rejected and the others are valid
- **WHEN** the goals part is imported
- **THEN** the valid goals are created and the rejected one reports as failed with its reason

#### Scenario: A failed goal is identifiable from its outcome

- **GIVEN** a goal that failed during import
- **WHEN** its outcome is read
- **THEN** it names the unit, the goal type, and the reason, without requiring a trace
  identifier to interpret
