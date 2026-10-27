## MODIFIED Requirements

### Requirement: Characters are read from the pinned datamine

Every `gameconfig/units/lineup.json` unit without the `MachineOfWar` trait SHALL be served as a character, grouped under `units-<faction>` (lowercased `FactionId`) and ordered by id. The catalog SHALL keep no hand-kept unit files.

#### Scenario: The shipped characters

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it serves 119 characters
