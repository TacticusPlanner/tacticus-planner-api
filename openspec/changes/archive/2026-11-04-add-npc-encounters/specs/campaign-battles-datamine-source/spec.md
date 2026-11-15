## MODIFIED Requirements

### Requirement: Battle enemies come from the opposing teams

A battle's enemies SHALL be the `npcId:index` units of every team whose `playerTeams` value is not 1, without board power-ups, counted per unit and sorted by npc id, then index. `stars` and `rank` SHALL come from the npc's stat row at the 1-based index, for bosses and every other npc, and `servedIndex` SHALL be that row's 0-based position. An index past the npc's ladder SHALL fail the load.

#### Scenario: Allied team

- **GIVEN** a battle whose second team has `playerTeams` 1
- **WHEN** the catalog loads
- **THEN** that team's units are not served as enemies or counted in `enemiesTotal`

#### Scenario: A regular npc's index is 1-based

- **GIVEN** a battle enemy `admecNpc1Vanguard:1`
- **WHEN** the catalog loads
- **THEN** its `stars` and `rank` come from that npc's first stat row and its `servedIndex` is 0
