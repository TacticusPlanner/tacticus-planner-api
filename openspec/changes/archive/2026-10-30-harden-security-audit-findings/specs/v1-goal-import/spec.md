## MODIFIED Requirements

### Requirement: Every source V1 goal receives exactly one outcome, plus one per synthesized prerequisite

This cardinality applies only when the goals part is not refused for missing
player data (see "The goals part is refused when the account has no player
data" above) — a refused import instead returns the single blocking outcome
described there, regardless of how many goals the V1 profile contains.

The import SHALL read at most the first 500 V1 goals in V1 priority order;
these are the *source* goals. When the V1 profile holds more, the import SHALL
append one skipped outcome with code `too_many_goals`, carrying no originating
V1 goal id, that states how many were not imported.

The response SHALL contain exactly one *source* outcome entry per source goal,
in V1 priority order, each carrying the id of the V1 goal it originated from.
When automatic prerequisite creation synthesizes a goal (Unlock or Ascension),
reports a prerequisite-target shortfall, or skips a prerequisite at the goal
cap, that SHALL add one further outcome entry with no originating V1 goal id,
appended after the source outcomes. Every outcome, source or synthesized, SHALL
carry a status of created, skipped, or failed; a stable machine-readable code;
a human-readable message; the entity type and entity id it concerned when
known; the V2 goal type it concerned when one was determined; and the id of the
V2 goal it created or matched when one applies.

The response SHALL list at most 1000 outcomes. When there would be more, the
first 999 SHALL be returned in the order above, followed by one skipped outcome
with code `outcomes_truncated` that states how many outcomes were not listed.

A client SHALL be able to count source-created goals and automatically
created prerequisite goals separately, using the presence or absence of an
originating V1 goal id: within the outcome bound, exactly one outcome exists
per source goal, and the number of created *source* outcomes SHALL equal the
number of V2 goals the import created from source goals. No count in the
response SHALL mix source goals with any other unit of aggregation.

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

#### Scenario: More V1 goals than the import reads

- **GIVEN** a V1 profile containing 501 goals, and automatic prerequisite creation not selected
- **WHEN** the goals part is imported
- **THEN** the 500 highest-priority goals each get a source outcome, and one further skipped
  outcome with code `too_many_goals` reports that 1 goal was not imported

## ADDED Requirements

### Requirement: The import respects the per-type goal cap

Each goal the import would create, source or synthesized prerequisite, SHALL take a slot under the cap of 200 non-archived goals per type (see `goal-creation`). Slots SHALL be counted under the profile lock and spent as goals are staged. A source goal with no slot left SHALL report skipped `goal_limit_reached`. A prerequisite with no slot left SHALL add a skipped `goal_limit_reached` outcome, and its dependents SHALL proceed without the edge.

#### Scenario: Goal type full during import

- **GIVEN** a profile with 199 non-archived Rank goals and a V1 profile with two importable Rank goals
- **WHEN** the goals part is imported
- **THEN** the higher-priority Rank goal is created and the other reports skipped with code `goal_limit_reached`

### Requirement: The import respects the project cap

The import's own project SHALL count toward the profile's cap of 100 non-archived projects (see `project-count-limit`). When the profile is already at the cap, the import SHALL create no project and no goal, and every goal it would have created SHALL report failed with code `project_limit_reached`.

#### Scenario: Project cap reached

- **GIVEN** a profile with 100 non-archived projects
- **WHEN** the goals part is imported
- **THEN** no project or goal is created, and every importable goal reports failed with code `project_limit_reached`
