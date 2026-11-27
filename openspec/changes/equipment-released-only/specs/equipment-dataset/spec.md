## MODIFIED Requirements

### Requirement: Equipment is built from the pinned datamine `items.json`

The served `equipment` dataset SHALL be built from the datamine copy `gameconfig/items.json` (opened with `DatamineFiles.Open`), one record per entry the game names, with `id` from the entry's key and `type` from `itemType`. An entry whose config `name` starts with `_` (the game's placeholder for an item it has not named, such as `_I_Block_C005`) SHALL be left out of the served dataset and of every character's `eligibleEquipment`. The catalog SHALL keep no hand-kept equipment raw file.

#### Scenario: The shipped items

- **WHEN** the `equipment` dataset is served at datamine `v1.44.88.3089`
- **THEN** it holds 205 records, and no record's `name` starts with `_`

#### Scenario: An unnamed placeholder item

- **WHEN** an `items.json` entry `I_Block_C005` has the config name `_I_Block_C005`
- **THEN** the served `equipment` dataset has no record with id `I_Block_C005`
