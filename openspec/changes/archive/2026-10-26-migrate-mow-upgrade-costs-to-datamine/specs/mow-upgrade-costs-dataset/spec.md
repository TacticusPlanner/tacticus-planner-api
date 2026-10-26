## ADDED Requirements

### Requirement: The MoW cost ladder is built from the pinned datamine file

The served `mow-upgrade-costs` dataset SHALL be built from the datamine copy `gameconfig/units/abilityUpgradeCostsMoW.json`, one record per row in file order. The catalog SHALL keep no hand-kept copy.

#### Scenario: Same ladder as before

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 59 records equal to the ones the retired `Data/mow-upgrade-costs.json` served, and its manifest hash is unchanged

### Requirement: Each record is keyed by the level it raises a MoW ability to

Each served record SHALL carry `level` (row position N, 0-based, plus 2), `gold`, `salvage`, `badges` (`rarity`, `amount`), `forgeBadges` (`rarity`, `amount`, or null) and `components`.

#### Scenario: The first and last rows

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** the first record is level 2 with gold 15, salvage 5, badges Common 1, no forge badges and 1 component, and the last is level 60 with gold 125000, badges Mythic 5, forge badges Mythic 5 and 50 components

### Requirement: Datamine keys map to the served fields

`gold` SHALL be served as `gold`, `dust` as `salvage` (absent = 0), `abilityToken{Rarity}` as `badges`, `itemAscensionResource_{Rarity}` as `forgeBadges` (absent = null) and `machinesOfWarToken` as `components`.

#### Scenario: A forge row

- **GIVEN** a row `{ gold: 300, abilityTokenEpic: 2, itemAscensionResource_Epic: 1, machinesOfWarToken: 16 }`
- **WHEN** it is read
- **THEN** it has gold 300, salvage 0, badges Epic 2, forge badges Epic 1 and 16 components

### Requirement: Unmappable rows fail startup

The catalog loader SHALL fail startup, naming the dataset and the row, when a row lacks `gold`, `machinesOfWarToken` or exactly one `abilityToken{Rarity}`, has more than one `itemAscensionResource_{Rarity}`, or has any other key.

#### Scenario: An unknown cost key

- **GIVEN** a row that also has `blessedSalvage: 1`
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `mow-upgrade-costs`
