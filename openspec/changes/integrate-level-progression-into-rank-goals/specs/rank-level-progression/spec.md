## Purpose

Defines the level a Rank or Ability target needs as an intrinsic, derived requirement of that target, with no Level goal type to plan it separately.

## ADDED Requirements

### Requirement: A target's level requirement is intrinsic and derived

A Character Rank goal SHALL include the character level required for its configured end rank and applied-upgrade target, and a Character Ability goal SHALL include the character level implied by the higher of its two ability targets, both derived from the current catalog ladder. Creation SHALL NOT require, synthesize, or accept a goal or dependency edge solely to satisfy that level. Rank completion SHALL still require the actual rank/slots target to be reached, and Ability completion its ability levels; reaching a level alone SHALL NOT complete either.

#### Scenario: Rank target is above current level

- **GIVEN** a character is below the level needed for a requested Rank target
- **WHEN** the Rank goal is created
- **THEN** the Rank goal retains its target with no new goal or dependency, and its required level is derived from that target

#### Scenario: Ability target is above current level

- **GIVEN** a Character Ability target implies a level above the character's current level
- **WHEN** the Ability goal is created
- **THEN** it is created without a Level goal or Level dependency, and its required level is derived from the target

### Requirement: The Level goal type does not exist

The goal API SHALL NOT expose a Level goal type or a Level target group on create, combined create, target edit, read, list, or import responses. A request that names the Level goal type SHALL be rejected with a validation error and SHALL NOT create or change any goal (an unrecognized `level` config member is ignored like any other unknown member). No goal SHALL declare a dependency on a Level goal.

#### Scenario: Creating a Level goal is rejected

- **WHEN** a client submits a goal whose goal type is Level
- **THEN** the request is rejected with a validation error and nothing is created

#### Scenario: Goal responses carry no Level data

- **WHEN** any goal is read or listed
- **THEN** its response contains no Level goal type and no Level target group

### Requirement: Existing Level goals are removed by migration

Applying the migration SHALL delete every existing Level goal, including its project memberships, and SHALL remove each deleted goal's id from every other goal's dependency list. Goals that depended on a Level goal SHALL otherwise be unchanged and remain valid. No Level goal SHALL be preserved, hidden, or reinterpreted as part of a Rank or Ability goal.

#### Scenario: Rank and Ability dependents survive

- **GIVEN** a Level goal that Rank and Ability goals depend on
- **WHEN** the migration runs
- **THEN** the Level goal and its memberships are gone, and the Rank and Ability goals remain with the Level id removed from their dependencies

#### Scenario: Standalone Level goal is deleted

- **GIVEN** a Level goal with no dependents
- **WHEN** the migration runs
- **THEN** it is deleted and no other goal changes
