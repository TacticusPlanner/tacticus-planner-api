## MODIFIED Requirements

### Requirement: Equipment is built from the pinned datamine `items.json`

The served `equipment` dataset SHALL be built from the datamine copy `gameconfig/items.json` (opened with `DatamineFiles.Open`), one record per entry, with `id` from the entry's key and `type` from `itemType`. The catalog SHALL keep no hand-kept equipment raw file.

#### Scenario: The shipped items

- **WHEN** the `equipment` dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 217 records

### Requirement: Equipment is served in type, then id order

The served array SHALL be ordered by `type`, then `id`, both ordinal.

#### Scenario: Type, then id

- **GIVEN** two items of one `type` with ids `x2` and `x1`, and an item whose `type` sorts before theirs
- **WHEN** the `equipment` dataset is served
- **THEN** the order is the earlier-typed item, `x1`, `x2`
