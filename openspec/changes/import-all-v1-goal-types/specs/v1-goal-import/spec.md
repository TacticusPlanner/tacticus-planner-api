## MODIFIED Requirements

### Requirement: Imported goals are created by the import operation

The import operation SHALL create the imported goals itself. It SHALL NOT return goal-creation requests for the caller to submit. Each import run that creates at least one goal SHALL create a new project of its own, named "Import" followed by the run's date and time in UTC (for example "Import 23 Oct 2026, 12:00 UTC"), and SHALL place every goal it creates in that project, each in the status its own V1 daily-planning choice implies (see `goal-lifecycle-status`). A run that creates no goals SHALL NOT create a project.

The import SHALL NOT require the caller to supply, or re-supply, anything derived from the V1 profile: V1 credentials are used once, within the same operation, and are not retained.

#### Scenario: Import creates goals in one operation

- **GIVEN** an account with player data and a V1 profile containing supported goals
- **WHEN** the goals part is imported
- **THEN** the goals exist on the account when the operation returns, and the response contains no goal-creation request for the caller to submit

#### Scenario: Imported goals land in a project of their own

- **GIVEN** an account with player data and a V1 profile containing supported goals
- **WHEN** the goals part is imported
- **THEN** a new project named after the import's date and time holds every goal the import created, and the default project gains none

#### Scenario: Import is repeatable without duplicating goals

- **GIVEN** an import has already created goals for an account
- **WHEN** the same V1 profile is imported again
- **THEN** no duplicate goals are created, each already-present goal reports as skipped because it already exists, and no new project is created

### Requirement: Skipped and not-imported outcomes are distinguishable

An outcome SHALL distinguish a goal that needed no import from a goal whose
information could not be carried across, and both from a goal that needs a
manual step from the user.

A goal SHALL report as skipped, with a code identifying which case applies,
when: its target is already reached according to the account's player data; the
account already has a goal of that type for that unit; it was merged into
another source goal's target for the same unit and type; or it is a V1
material goal that needs a manual step (see "V1 material goals become Upgrade
goals at their V1 priority").

A goal SHALL report as not imported, with a code identifying which case
applies, when: its V1 goal type is not one V1 defines; its unit is not present in
the game catalog; its upgrade material is not present in the game catalog; no
unit on the account uses its upgrade material; its target does not exist on the
V2 progression ladder; or it carries no usable target.

#### Scenario: Already-reached target is a skip

- **GIVEN** a V1 Rank goal whose target rank is at or below the character's current rank
- **WHEN** the goals part is imported
- **THEN** that goal reports as skipped with a code identifying an already-reached target

#### Scenario: Unknown V1 goal type is not imported

- **GIVEN** a V1 goal whose type is not one V1 defines
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

When V1 contains multiple Rank goals for one unit, the import SHALL preserve goals with distinct normalized end targets as separate V2 milestones in their V1 priority order. Exact Rank end-target duplicates SHALL merge into the highest-priority survivor and report the survivor id. For non-Rank goals, the existing one-per-unit/type merge remains: Ascension spans lowest start to highest end; other types retain the highest-priority source. Every merged-away source SHALL report a skip with merge code and survivor id.

#### Scenario: Two rank goals merge into one spanning goal

- **GIVEN** two V1 Rank goals for one character with the same normalized end target but different starts
- **WHEN** goals are imported
- **THEN** one Rank goal survives, with a span covering both starts, and the other source reports a merge skip naming that goal

#### Scenario: Duplicate ability goals keep the higher-priority one

- **GIVEN** two V1 Ability goals for one unit
- **WHEN** goals are imported
- **THEN** the higher-priority one reports created and the other reports skipped with a merge code

#### Scenario: Distinct Rank targets import separately

- **GIVEN** V1 has Rank goals for Bellator targeting Silver3 and Gold1
- **WHEN** goals are imported
- **THEN** two V2 goals are created with distinct ids and preserved V1 order

## ADDED Requirements

### Requirement: A V1 Machine of War Ascension goal stored under its character field imports as a Mow goal

V1 stores every Ascension goal's unit in its character field, Machines of War included. When that
identifier matches no catalog character but matches a catalog Machine of War, the goal SHALL import as a
Machine of War Ascension goal.

#### Scenario: Mow Ascension stored under character

- **GIVEN** a V1 Ascension goal whose character field is a Machine of War id
- **WHEN** the goals part is imported
- **THEN** a Machine of War Ascension goal is created for that unit

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
  goals, then the roster's characters, then its Machines of War; among those, a unit whose existing Upgrade
  goal already targets the material first, then a unit with no Upgrade goal — reported with a code that
  says the unit was suggested.

A unit holds one in-flight Upgrade goal, so material goals landing on the same unit SHALL merge into one
Upgrade goal at the earliest one's priority (a repeated material keeps the larger quantity). A material for a
unit whose existing Upgrade goal targets other materials, and a pre-farm goal none of whose linked goals was
imported, SHALL report as skipped with a code identifying a required manual step, carrying the material, the
quantity and a unit. A material no roster unit uses SHALL report as not imported with a code identifying that
no unit uses it.

#### Scenario: Pre-farm goal becomes an Upgrade goal per linked unit

- **GIVEN** a V1 pre-farm goal at priority 1 linked to Rank goals for two characters at priorities 2 and 3, and to an id no longer present
- **WHEN** the goals part is imported
- **THEN** an Upgrade goal for the material is created on each character, in link order, both ahead of the Rank goals, each for what that character's Rank goal needs

#### Scenario: Upgrade-material goal goes on a suggested unit at its priority

- **GIVEN** a V1 Rank goal for a character and a V1 upgrade-material goal for a material on that character's rank ladder, followed by another unit's goal
- **WHEN** the goals part is imported
- **THEN** an Upgrade goal for the material and quantity is created on that character between the two other goals, reported as placed on a suggested unit

#### Scenario: Material goals on one unit merge

- **GIVEN** a V1 pre-farm goal and a V1 upgrade-material goal that both land on the same character
- **WHEN** the goals part is imported
- **THEN** one Upgrade goal targets both materials, and the later goal reports as merged into it

#### Scenario: Material nobody uses is not imported

- **GIVEN** a V1 upgrade-material goal whose material no roster unit uses
- **WHEN** the goals part is imported
- **THEN** that goal reports as not imported with a code identifying that no unit uses the material
