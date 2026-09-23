## Purpose

Defines Rank's inherent level prerequisite without requiring a second goal record for routine progression, while preserving independently useful Level goals.

## ADDED Requirements

### Requirement: Rank's level gate is intrinsic to its target

A Character Rank goal SHALL include the character level required for its configured end rank and applied-upgrade target as part of that goal's progression. Creation SHALL NOT require or synthesize a Level goal solely to satisfy that Rank target. Rank completion SHALL still require the actual rank/slots target to be reached; reaching the level alone SHALL NOT complete it.

#### Scenario: Rank target is above current level

- **GIVEN** a character is below the level needed for a requested Rank target
- **WHEN** the Rank goal is created
- **THEN** the Rank goal retains its target without a new Level dependency, and its required level is derived from that target

#### Scenario: Standalone Level goal remains supported

- **WHEN** a user creates an intentional Level goal without a Rank dependency
- **THEN** that goal remains separately addressable and managed as a Level target

### Requirement: Legacy Level dependencies are not destructively guessed away

An existing Rank→Level dependency whose Level goal has no other dependent SHALL be interpreted as routine Rank progression for planning and SHALL not cause an independent required Level milestone or duplicate XP allocation. An existing Level goal with an Ability dependent or an independent purpose SHALL remain a separately actionable goal. No existing Level record SHALL be deleted solely by inference from a dependency edge.

#### Scenario: Rank-only legacy pair

- **GIVEN** a Rank goal depends on a Level goal that has no other dependent
- **WHEN** an effective plan is derived
- **THEN** level progression is attributed to the Rank milestone once and the stored Level goal remains recoverable by id

#### Scenario: Shared Ability prerequisite

- **GIVEN** one Level goal supports both Rank and Ability goals
- **WHEN** the effective plan is derived
- **THEN** the Level goal remains separately actionable for Ability and the Rank does not claim its XP twice
