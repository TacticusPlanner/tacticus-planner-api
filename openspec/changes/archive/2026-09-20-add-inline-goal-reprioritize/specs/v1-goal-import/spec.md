## REMOVED Requirements

### Requirement: Imported goals preserve V1 unit order

**Reason**: This requirement's entire rationale for collapsing V1's interleaved goal priority into unit blocks was "because a project orders whole unit blocks" — that no longer holds once priority is flat per-goal. Flat ordering can now preserve V1's actual priority sequence exactly, goal by goal, which is strictly more faithful than the collapsed unit-block approximation this requirement described. Replaced by "Imported goals preserve V1 priority order" under ADDED Requirements below.

**Migration**: A profile imported after this change lands will order its goals by the exact V1 priority sequence (with created prerequisites placed immediately before the goal(s) that needed them), not by unit blocks in first-appearance order. A profile already imported under the old behavior is unaffected — this only changes future imports, not existing data.

## MODIFIED Requirements

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
goal that required it, and SHALL be placed immediately before them in the
constructed import sequence. There is no automatic within-unit reordering to
fall back on for this placement — the import process itself is responsible
for the prerequisite's position, not a downstream ordering step.

A prerequisite SHALL NOT be created when the unit's imported goals already
include one of that type, or when the account already has one of that type for
that unit. In those cases the requirement SHALL be reported rather than
satisfied, and the existing goal's target SHALL NOT be altered.

A synthesized Unlock or Level goal SHALL be validated by the same rules a
manual creation of that goal would be before it is persisted (e.g. Unlock is
valid only for a Character with catalog shard-upgrade data; a Level target
must not exceed the character-level cap). When that validation fails, the
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

- **GIVEN** a V1 Rank goal whose target is within the character's progression-derived cap and
  current level
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** no Ascension or Level goal is created for that character

#### Scenario: A created prerequisite precedes its dependents in the import sequence

- **GIVEN** an imported Rank goal requires a Level prerequisite the account doesn't have
- **WHEN** the prerequisite is created automatically
- **THEN** the created Level goal's position in the project's priority order precedes the Rank goal's position

## ADDED Requirements

### Requirement: Imported goals preserve V1 priority order

Goals SHALL be created in ascending V1 priority order, and the resulting project order SHALL preserve that exact V1 priority sequence, goal by goal — including any interleaving between different units' goals that V1's original priority expressed. A created prerequisite (see "Missing prerequisites are created automatically...") is placed immediately before the goal(s) that required it, which may shift it earlier than its own original V1 position, since it didn't exist as a separate goal in V1.

#### Scenario: V1's goal interleaving is preserved

- **GIVEN** a V1 profile whose priority sequence interleaves goals for characters A and B (for example: A, B, A)
- **WHEN** the goals part is imported
- **THEN** the project's resulting priority order preserves that same interleaved sequence for A and B's goals

#### Scenario: A created prerequisite is placed ahead of its own V1-relative position

- **GIVEN** a V1 profile's imported Rank goal for character A requires a Level prerequisite absent from the account, and A's Rank goal is not first in V1's priority sequence
- **WHEN** the goals part is imported with automatic prerequisite creation
- **THEN** the created Level goal is positioned immediately before A's Rank goal, ahead of goals that preceded A's Rank goal in V1's original sequence but did not require this prerequisite
