# v1-goal-import Specification

## Purpose
Defines how a V1 planner profile's goals are imported into V2: which V1 goals
become which V2 goals, how their targets and shard sources are derived, when a
goal is skipped rather than not imported, the order goals are created in,
automatic creation of missing prerequisites, and the per-goal outcome report
the caller receives.

## Requirements

### Requirement: Imported goals are created by the import operation

The import operation SHALL create the imported goals itself. It SHALL NOT return goal-creation requests for the caller to submit. Each import run that creates at least one goal SHALL create a new project of its own, named "V1 import · " followed by the run's UTC date and time (for example "V1 import · 23 Oct, 13:45 UTC"), SHALL place every goal it creates in that project, each in the status its own V1 daily-planning choice implies (see `goal-lifecycle-status`), and SHALL return that project's id in the response. A run that creates no goals SHALL NOT create a project, and returns no project id.

The import SHALL NOT require the caller to supply, or re-supply, anything derived from the V1 profile: V1 credentials are used once, within the same operation, and are not retained.

#### Scenario: Import creates goals in one operation

- **GIVEN** an account with player data and a V1 profile containing supported goals
- **WHEN** the goals part is imported
- **THEN** the goals exist on the account when the operation returns, and the response contains no goal-creation request for the caller to submit

#### Scenario: Imported goals land in a project of their own

- **GIVEN** an account with player data and a V1 profile containing supported goals
- **WHEN** the goals part is imported
- **THEN** a new project named after the import's date holds every goal the import created, the response names that project, and the default project gains none

#### Scenario: Import is repeatable without duplicating goals

- **GIVEN** an import has already created goals for an account
- **WHEN** the same V1 profile is imported again
- **THEN** no duplicate goals are created, each already-present goal reports as skipped because it already exists, and no new project is created

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

An imported goal SHALL carry notes from its V1 source. When source goals merge into one V2 goal, the merged goal SHALL carry notes from every contributing source that had notes. Distinct Rank milestones SHALL keep their own notes rather than combining them.

#### Scenario: Notes survive the import

- **GIVEN** a V1 goal with notes
- **WHEN** it is imported
- **THEN** the created goal carries those notes

#### Scenario: Merged goals combine their notes

- **GIVEN** two merging V1 Ascension goals for one character, both with notes
- **WHEN** they are imported
- **THEN** the created goal's notes contain both source goals' notes

#### Scenario: Distinct Rank notes stay distinct

- **GIVEN** V1 Silver3 and Gold1 Rank goals have different notes
- **WHEN** they are imported
- **THEN** each V2 milestone carries only its own notes

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

