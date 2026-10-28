# equipment-dataset Specification

## Purpose

Describes the served `equipment` dataset: its source (the pinned datamine `items.json`), the field and stat-name
mapping, each item's upgrade ladder, and the served order.

## Requirements

### Requirement: Equipment is built from the pinned datamine `items.json`

The served `equipment` dataset SHALL be built from the datamine copy `gameconfig/items.json` (opened with `DatamineFiles.Open`), one record per entry, with `id` from the entry's key and `type` from `itemType`. The catalog SHALL keep no hand-kept equipment raw file.

#### Scenario: The shipped items

- **WHEN** the `equipment` dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 217 records

### Requirement: Optional item fields have served defaults

`name`, `rarity`, `abilityId`, `isRelic`, `isUniqueRelic`, `allowedUnits` and `allowedFactions` SHALL be served from the entry. An absent `abilityId` SHALL be served as `""`, absent relic flags as `false`, and absent allowed lists as `[]`.

#### Scenario: A plain item

- **WHEN** an `items.json` entry has no `abilityId`, relic flags or allowed lists
- **THEN** its record has `abilityId: ""`, `isRelic: false`, `isUniqueRelic: false`, `allowedUnits: []` and `allowedFactions: []`

### Requirement: Level stats keep the served stat names

Each served `levels[]` entry SHALL carry `stats`: the entry level's stat map with `critDmg`, `critDmgBonus`, `blockDmg`, `blockDmgBonus` and `fixedArmor` served as `critDamage`, `critDamageBonus`, `blockDamage`, `blockDamageBonus` and `armor`, and every other key and every value unchanged.

#### Scenario: A crit item level

- **WHEN** a level's stats are `{ critChance: 5, critDmg: 10 }`
- **THEN** the served level's stats are `{ critChance: 5, critDamage: 10 }`

### Requirement: Each item carries its own upgrade ladder

Each served record SHALL carry `upgradeLevels`, one entry per level in order, each `{ goldCost, salvageCost, mythicSalvageCost }` from that level's `goldCost`, `dustCost` and `mythicDustCost`, an absent cost served as `0`.

#### Scenario: A mythic level

- **WHEN** a level has `goldCost: 200, dustCost: 30, mythicDustCost: 4`
- **THEN** its `upgradeLevels` entry is `{ goldCost: 200, salvageCost: 30, mythicSalvageCost: 4 }`

#### Scenario: One ladder per rarity in 1.43

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** every item of a rarity has the same `upgradeLevels`, equal to the retired `equipment-upgrade-costs.json` ladder for that rarity

### Requirement: Equipment is served in type, then id order

The served array SHALL be ordered by `type`, then `id`, both ordinal.

#### Scenario: Type, then id

- **GIVEN** two items of one `type` with ids `x2` and `x1`, and an item whose `type` sorts before theirs
- **WHEN** the `equipment` dataset is served
- **THEN** the order is the earlier-typed item, `x1`, `x2`
