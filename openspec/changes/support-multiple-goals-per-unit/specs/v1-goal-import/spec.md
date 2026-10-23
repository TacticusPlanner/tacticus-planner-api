## MODIFIED Requirements

### Requirement: Duplicate V1 goals for one unit and type are merged and reported

When V1 contains multiple goals of one type for one unit, the import SHALL preserve goals with distinct target keys (`goal-target-milestones`) as separate V2 goals in their V1 priority order. This applies to Rank, Ascension, Ability and Upgrade. Only exact target-key duplicates SHALL merge into the highest-priority survivor: Rank and Ascension duplicates span the lowest start to the shared end (Ascension also unions acquisition sources); other types retain the highest-priority source. Unlock goals for one unit always merge. Every merged-away source SHALL report a skip with merge code and survivor id.

#### Scenario: Two rank goals merge into one spanning goal

- **GIVEN** two V1 Rank goals for one character with the same normalized end target but different starts
- **WHEN** goals are imported
- **THEN** one Rank goal survives, with a span covering both starts, and the other source reports a merge skip naming that goal

#### Scenario: Duplicate ability goals keep the higher-priority one

- **GIVEN** two V1 Ability goals for one unit with the same active and passive end
- **WHEN** goals are imported
- **THEN** the higher-priority one reports created and the other reports skipped with a merge code

#### Scenario: Distinct Rank targets import separately

- **GIVEN** V1 has Rank goals for Bellator targeting Silver3 and Gold1
- **WHEN** goals are imported
- **THEN** two V2 goals are created with distinct ids and preserved V1 order

#### Scenario: Distinct Ascension and Ability targets import separately

- **GIVEN** V1 has Ascension goals for Ragnar ending at Legendary and Mythic, and Ability goals ending at 40/40 and 50/50
- **WHEN** goals are imported
- **THEN** four V2 goals are created, each with its own notes, in V1 priority order

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
requires it. There is no automatic within-unit reordering to
fall back on for this placement — the import process itself is responsible
for the prerequisite's position, not a downstream ordering step.

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

### Requirement: V1 material goals become Upgrade goals at their V1 priority

V1's pre-farm goals (one material, reserved for chosen goals) and upgrade-material goals (one material, one
quantity) have no unit, while every V2 goal belongs to one. Each SHALL become Upgrade goals placed at the V1
goal's own priority:

- A pre-farm goal SHALL become one Upgrade goal per distinct unit of its linked goals that were imported,
  in link order and consecutively from its priority, each targeting as much of the material as that unit
  still needs for its linked goals (a Character from its live rank to its highest linked Rank target, else
  its whole remaining ladder; a Machine of War from its live ability levels to its linked Ability targets,
  else both whole tracks; crafted upgrades counted through their recipes). A unit that no longer needs the
  material SHALL be left out; when none does, the goal SHALL report as skipped as already reached.
- An upgrade-material goal SHALL become an Upgrade goal for its quantity on a suggested unit that uses the
  material — in order of preference, the first unit by V1 priority among the profile's own imported unit
  goals (whether or not the unit is on the roster), then the roster's characters, then its Machines of
  War — reported with a code that says the unit was suggested.

Material goals landing on the same unit with the same material SHALL merge into one Upgrade goal at the
earliest one's priority, keeping the larger quantity. Material goals with different materials SHALL stay
separate Upgrade goals at their own V1 priorities (`goal-target-milestones`), and a unit's existing Upgrade
goal for other materials SHALL NOT block one. A pre-farm goal none of whose linked goals was imported SHALL
report as skipped with a code identifying a required manual step, carrying the material, the unit chosen by
the suggested-unit rule above and a null quantity (a V1 pre-farm goal has no fixed quantity). A material that no unit in that same
owner set (imported unit goals, roster characters, roster Machines of War) uses SHALL report as not imported
with a code identifying that no unit uses it.

#### Scenario: Pre-farm goal becomes an Upgrade goal per linked unit

- **GIVEN** a V1 pre-farm goal at priority 1 linked to Rank goals for two characters at priorities 2 and 3, and to an id no longer present
- **WHEN** the goals part is imported
- **THEN** an Upgrade goal for the material is created on each character, in link order, both ahead of the Rank goals, each for what that character's Rank goal needs

#### Scenario: Upgrade-material goal goes on a suggested unit at its priority

- **GIVEN** a V1 Rank goal for a character and a V1 upgrade-material goal for a material on that character's rank ladder, followed by another unit's goal
- **WHEN** the goals part is imported
- **THEN** an Upgrade goal for the material and quantity is created on that character between the two other goals, reported as placed on a suggested unit

#### Scenario: Material goals on one unit merge

- **GIVEN** a V1 pre-farm goal and a V1 upgrade-material goal for the same material that both land on the same character
- **WHEN** the goals part is imported
- **THEN** one Upgrade goal targets the material at the larger quantity, and the later goal reports as merged into it

#### Scenario: Different materials on one unit stay separate

- **GIVEN** two V1 upgrade-material goals for different materials at priorities 2 and 7 that land on the same character, which already has an in-flight Upgrade goal for a third material
- **WHEN** the goals part is imported
- **THEN** two new Upgrade goals are created at those priorities, the existing goal is unchanged, and no manual-step outcome is reported

#### Scenario: Material nobody uses is not imported

- **GIVEN** a V1 upgrade-material goal whose material no imported unit goal's unit and no roster unit uses
- **WHEN** the goals part is imported
- **THEN** that goal reports as not imported with a code identifying that no unit uses the material

#### Scenario: An imported goal's unit off the roster can own a material

- **GIVEN** a V1 Unlock goal for a character not on the roster, and a V1 upgrade-material goal for a material only that character uses
- **WHEN** the goals part is imported
- **THEN** the Upgrade goal is created on that character with the suggested-unit code, and no "no unit uses" outcome is reported
