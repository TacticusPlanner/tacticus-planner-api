## MODIFIED Requirements

### Requirement: Missing prerequisites are created automatically by the same rules as manual creation

When automatic prerequisite creation is selected, the import SHALL create the
prerequisite goals a manual creation of the same target would suggest, using
the same rules and the same minimum targets:

- an **Unlock** goal when the unit is absent from the account's roster and the
  imported goal requires the unit to exist;
- an **Ascension** goal when an imported target is above the unit's
  progression-derived cap, targeting the lowest progression that satisfies
  every such target for that unit that no covering Ascension goal satisfies.

The character level a Rank or Ability target needs is intrinsic to that target
(see `rank-level-progression`) and SHALL NOT synthesize any goal.

An Ascension requirement SHALL first be satisfied by a covering Ascension goal:
an imported or already-existing in-flight Ascension goal for that unit whose end
reaches the required progression (the lowest such end when several do). An
imported Ascension goal is considered after duplicate merging, so the dependency
SHALL reference the surviving persisted goal, never a merged-away source. The
dependent goal SHALL declare a dependency on it and no goal is created. Only
when no Ascension goal covers the requirement SHALL a new one be created, and
then only if no in-flight Ascension goal for the unit already has that exact
end. Existing Ascension targets SHALL NOT be altered.

An Unlock prerequisite SHALL NOT be created when the unit's imported goals
already include an Unlock goal, or when the account already has one for that
unit; in those cases the requirement SHALL be reported rather than satisfied.

Each created prerequisite SHALL be declared as a dependency of every imported
goal that required it, and SHALL be placed immediately before the first of
them in the constructed import sequence, so it precedes every goal that
requires it.

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

- **GIVEN** a V1 profile with a Rank goal needing Legendary and an Ascension goal for the same
  character ending at Legendary
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** no Ascension goal is synthesized and the Rank goal declares a dependency on the
  imported Ascension goal

#### Scenario: A too-low Ascension goal is kept and a covering one is added

- **GIVEN** a V1 profile with a Rank goal needing Legendary and an Ascension goal for the same
  character ending at Epic
- **WHEN** the goals part is imported with automatic prerequisites selected
- **THEN** the Epic Ascension goal is imported unchanged, a separate Ascension goal ending at
  the lowest Legendary progression is created ahead of the Rank goal, and the Rank goal depends
  on it

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
