## MODIFIED Requirements

### Requirement: Every source V1 goal receives exactly one outcome, plus one per synthesized prerequisite

This cardinality applies only when the goals part is not refused for missing
player data (see "The goals part is refused when the account has no player
data" above) — a refused import instead returns the single blocking outcome
described there, regardless of how many goals the V1 profile contains.

The response SHALL contain exactly one *source* outcome entry per goal present
in the V1 profile, in V1 priority order, each carrying the id of the V1 goal
it originated from. When automatic prerequisite creation synthesizes a goal
(Unlock or Ascension) or reports a prerequisite-target shortfall, that
SHALL add one further outcome entry with no originating V1 goal id, appended
after the source outcomes. Every outcome, source or synthesized, SHALL carry a
status of created, skipped, or failed; a stable machine-readable code; a
human-readable message; the entity type and entity id it concerned when known;
the V2 goal type it concerned when one was determined; and the id of the V2
goal it created or matched when one applies.

A client SHALL be able to count source-created goals and automatically
created prerequisite goals separately, using the presence or absence of an
originating V1 goal id: exactly one outcome exists per source V1 goal, and the
number of created *source* outcomes SHALL equal the number of V2 goals the
import created from source goals. No count in the response SHALL mix source
goals with any other unit of aggregation.

#### Scenario: Outcome count matches the source profile

- **GIVEN** a V1 profile containing 34 goals, and automatic prerequisite
  creation not selected (so no synthesized outcomes are added)
- **WHEN** the goals part is imported
- **THEN** the response contains exactly 34 outcome entries, one per source goal

#### Scenario: A synthesized prerequisite adds an outcome beyond the source count

- **GIVEN** a V1 profile containing 1 goal, and automatic prerequisite creation
  selected for a target that needs one synthesized Unlock goal
- **WHEN** the goals part is imported
- **THEN** the response contains 2 outcome entries: 1 source outcome carrying
  that goal's V1 id, and 1 synthesized-prerequisite outcome carrying none

#### Scenario: Several goals for one unit are reported individually

- **GIVEN** a V1 profile containing a Rank goal and an Ascension goal for the same character
- **WHEN** both are imported successfully
- **THEN** two created outcomes are reported, each naming its own goal type and created goal id

#### Scenario: Outcomes follow V1 priority order

- **GIVEN** a V1 profile whose goals have distinct priorities
- **WHEN** the goals part is imported
- **THEN** the outcome entries appear in ascending V1 priority order

### Requirement: Imported goals preserve V1 priority order

Goals SHALL be created in ascending V1 priority order, and the resulting project order SHALL preserve that exact V1 priority sequence, goal by goal — including any interleaving between different units' goals that V1's original priority expressed. A created prerequisite (see "Missing prerequisites are created automatically...") is placed immediately before the goal(s) that required it, which may shift it earlier than its own original V1 position, since it didn't exist as a separate goal in V1.

#### Scenario: V1's goal interleaving is preserved

- **GIVEN** a V1 profile whose priority sequence interleaves goals for characters A and B (for example: A, B, A)
- **WHEN** the goals part is imported
- **THEN** the project's resulting priority order preserves that same interleaved sequence for A and B's goals

#### Scenario: A created prerequisite is placed ahead of its own V1-relative position

- **GIVEN** a V1 profile's imported Rank goal for character A requires an Ascension prerequisite absent from the account, and A's Rank goal is not first in V1's priority sequence
- **WHEN** the goals part is imported with automatic prerequisite creation
- **THEN** the created Ascension goal is positioned immediately before A's Rank goal, ahead of goals that preceded A's Rank goal in V1's original sequence but did not require this prerequisite

### Requirement: Missing prerequisites are created automatically by the same rules as manual creation

When automatic prerequisite creation is selected, the import SHALL create the
prerequisite goals a manual creation of the same target would suggest, using
the same rules and the same minimum targets:

- an **Unlock** goal when the unit is absent from the account's roster and the
  imported goal requires the unit to exist;
- an **Ascension** goal when an imported target is above the unit's
  progression-derived cap, targeting the lowest progression that satisfies
  every such target for that unit.

The character level a Rank or Ability target needs is intrinsic to that target
(see `rank-level-progression`) and SHALL NOT synthesize any goal.

Each created prerequisite SHALL be declared as a dependency of every imported
goal that required it, and SHALL be placed immediately before them in the
constructed import sequence. There is no automatic within-unit reordering to
fall back on for this placement — the import process itself is responsible
for the prerequisite's position, not a downstream ordering step.

A prerequisite SHALL NOT be created when the unit's imported goals already
include one of that type, or when the account already has one of that type for
that unit. In those cases the requirement SHALL be reported rather than
satisfied, and the existing goal's target SHALL NOT be altered.

A synthesized Unlock goal SHALL be validated by the same rules a manual
creation of that goal would be before it is persisted (Unlock is valid only for
a Character with catalog shard-upgrade data). When that validation fails, the
prerequisite SHALL NOT be created; the requirement SHALL be reported as
failed, and the imported goals that needed it proceed without a dependency
edge to it.

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

- **GIVEN** a V1 Rank goal whose target is within the character's progression-derived cap, with
  the character below the level the target needs
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** no Ascension goal is created and no other goal is created for that character

#### Scenario: A created prerequisite precedes its dependents in the import sequence

- **GIVEN** an imported Rank goal requires an Ascension prerequisite the account doesn't have
- **WHEN** the prerequisite is created automatically
- **THEN** the created Ascension goal's position in the project's priority order precedes the Rank goal's position

#### Scenario: Rank and Ability imports do not synthesize Level

- **GIVEN** imported Rank and Ability targets that need a higher character level
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** no Level goal is created, and each target still carries its own level requirement
