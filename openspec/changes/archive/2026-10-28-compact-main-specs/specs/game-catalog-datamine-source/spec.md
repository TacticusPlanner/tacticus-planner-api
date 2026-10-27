## ADDED Requirements

### Requirement: The catalog game version is the pinned datamine's build

The catalog `GameVersion` SHALL be the trimmed contents of the pinned datamine copy `raw/VERSION` (the game build the datamine was extracted from). Loading SHALL fail when that file is empty.

#### Scenario: Game version follows the pin

- **GIVEN** the pinned `raw/VERSION` holds `1.43.157.2989`
- **WHEN** the game catalog manifest is requested
- **THEN** its `GameVersion` is `1.43.157.2989`

## REMOVED Requirements

### Requirement: Adding the datamine source changes no served data

**Reason**: L1 — one-off transition rule ("Applying this change SHALL NOT…", "after this change").

**Migration**: None.
