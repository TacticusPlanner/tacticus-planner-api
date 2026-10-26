# upgrades-dataset Specification

## Purpose
Describes where the served `upgrades` dataset's upgrade materials come from: the pinned datamine's upgrade table, named from its English strings, with the stat mapping and grouping that keep the served shape and order.

## Requirements

### Requirement: Upgrade materials are built from the pinned datamine files

The served `upgrades` dataset SHALL be built from the datamine copies `gameconfig/upgrades.json` (keyed by upgrade id) and `languages/en.json`. The catalog SHALL keep no hand-kept upgrade files.

#### Scenario: Same materials plus the game's new one

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 560 records: the 559 the retired `Data/upgrades/*` files served, unchanged, plus `upgArmL008` (Transdimensional Sanctum, Legendary, Armour, not craftable)

### Requirement: Upgrade fields map from the game's

For each upgrade:

- `id` and `snowprintId` SHALL be the key.
- `label` and `material` SHALL be the English string `Upgrades/{id}_name`.
- `rarity` SHALL be the game's rarity.
- `stat` SHALL map `hp` to `Health`, `dmg` to `Damage` and `fixedArmor` to `Armour`.
- `craftable` SHALL be whether `crafting` is present, and `recipe` SHALL be its entries (`id` as `material`, `amount` as `count`).

#### Scenario: Accented names come from the English strings

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** `upgHpL111` has label `Mjød`, not the developer name `Mjod`

#### Scenario: A craftable upgrade

- **GIVEN** `upgHpU004C` with `crafting: [{ id: "upgHpC002", amount: 6 }]`
- **WHEN** it is read
- **THEN** it is craftable with a recipe of 6 `upgHpC002`

### Requirement: The served order is stable

Upgrades SHALL be grouped as `upgrades-{rarity}` (base) and `upgrades-{rarity}-crafted` (craftable), keys in ordinal order, each group ordered by id, matching the order the retired files gave.

#### Scenario: Grouping

- **GIVEN** an Uncommon base upgrade and two Uncommon craftable ones
- **WHEN** they are read
- **THEN** they fall under `upgrades-uncommon` and `upgrades-uncommon-crafted`, each ordered by id

### Requirement: An unnamed or unknown upgrade fails startup

Loading SHALL fail, naming the dataset and the upgrade, when an upgrade has no English name or a `statType` outside the mapping.

#### Scenario: Unknown stat

- **GIVEN** an upgrade with `statType` `speed`
- **WHEN** the catalog loads
- **THEN** loading fails naming `upgrades` and the upgrade id
