## MODIFIED Requirements

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
