## MODIFIED Requirements

### Requirement: The MoW cost ladder is built from the pinned datamine file

The served `mow-upgrade-costs` dataset SHALL be built from the datamine copy `gameconfig/units/abilityUpgradeCostsMoW.json`, one record per row in file order. The catalog SHALL keep no hand-kept copy.

#### Scenario: The shipped ladder

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 59 records
