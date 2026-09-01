## Purpose

Lets clients order project work by Character/MoW while preserving one deterministic flattened goal order for scheduling engines.

## Requirements

### Requirement: Project unit order is addressable through a dedicated operation

The API SHALL provide an authenticated operation that accepts the complete ordered set of distinct Active/Paused Character/MoW keys currently in an owned project. It SHALL change priority only and SHALL NOT replace membership.

#### Scenario: Reorder two units

- **GIVEN** a project contains Ragnar and Aun'shi unit blocks
- **WHEN** the caller submits Aun'shi before Ragnar
- **THEN** all Aun'shi goals precede all Ragnar goals in the returned project-goal order

#### Scenario: Stale unit set is rejected atomically

- **GIVEN** membership changed after the client loaded its unit list
- **WHEN** it submits a missing or extra unit set
- **THEN** the API rejects the request without changing priorities

### Requirement: Goals inside a unit are ordered automatically

The API SHALL place dependencies before dependents. Unrelated existing goals SHALL preserve prior relative order; new unrelated goals SHALL append inside their unit; goal id SHALL provide a deterministic final tie-breaker.

#### Scenario: Dependency order overrides prior number

- **GIVEN** Rank depends on Ascension for one unit
- **WHEN** order is normalized
- **THEN** Ascension precedes Rank

#### Scenario: New goal joins existing block

- **GIVEN** a unit already has goals in a project
- **WHEN** an unrelated goal is added
- **THEN** it appends within that block without moving the block

### Requirement: Flattened order remains the canonical scheduler order

Project-goal list responses SHALL return Active/Paused goals contiguously by submitted unit order and automatic inner order. Completed/Archived memberships SHALL follow in stable prior order.

#### Scenario: Scheduler receives contiguous unit blocks

- **WHEN** ordered project goals are listed after reprioritization
- **THEN** no lower-priority unit goal appears between goals of a higher-priority unit

### Requirement: Goal creation does not accept numeric priority

Single and combined goal creation SHALL accept project membership without caller-authored numeric priority. The API SHALL place new goals automatically into unit order.

#### Scenario: First goal for new unit appends unit

- **WHEN** a goal introduces a unit not already in the project
- **THEN** the new unit block is appended after existing in-flight units
