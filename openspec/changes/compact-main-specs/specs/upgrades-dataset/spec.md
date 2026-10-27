## MODIFIED Requirements

### Requirement: Upgrade materials are built from the pinned datamine files

The served `upgrades` dataset SHALL be built from the datamine copies `gameconfig/upgrades.json` (keyed by upgrade id). The catalog SHALL keep no hand-kept upgrade files.

#### Scenario: Same materials plus the game's new one

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 560 records: the 559 the retired `Data/upgrades/*` files served, unchanged except two labels that lose their accents, plus `upgArmL008` (Transdimensional Sanctum, Legendary, Armour, not craftable)

#### Scenario: The shipped materials

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 560 records, including `upgArmL008` (Legendary, Armour, not craftable)

### Requirement: The served order is stable

Upgrades SHALL be grouped as `upgrades-{rarity}` (base) and `upgrades-{rarity}-crafted` (craftable), keys in ordinal order, each group ordered by id.

#### Scenario: Grouping

- **GIVEN** an Uncommon base upgrade and two Uncommon craftable ones
- **WHEN** they are read
- **THEN** they fall under `upgrades-uncommon` and `upgrades-uncommon-crafted`, each ordered by id
