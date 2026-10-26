# ascension-costs-dataset Specification

## Purpose
Describes the served `ascension-costs` dataset: the shards, mythic shards and orbs needed to reach each step of the shared character ascension ladder, built from the pinned datamine.

## Requirements

### Requirement: The ascension ladder is built from the pinned datamine file

The served `ascension-costs` dataset SHALL be built from the datamine copy `gameconfig/units/heroProgressionSteps.json`, one record per row in file order. The catalog SHALL keep no hand-kept copy.

#### Scenario: Same ladder as before

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds the same 20 records the retired `Data/ascension-costs.json` served, and its manifest hash is unchanged

### Requirement: Each row's stars map to a progression

A record's `progression` SHALL be `{rarity}:{label}`, where the row's `stars` 0 to 14 map in order to `None`, `OneStar`, `TwoStars`, `ThreeStars`, `FourStars`, `FiveStars`, `RedOneStar`, `RedTwoStars`, `RedThreeStars`, `RedFourStars`, `RedFiveStars`, `OneBlueStar`, `TwoBlueStars`, `ThreeBlueStars` and `MythicWings`.

#### Scenario: Blue stars

- **GIVEN** a row with `stars: 11` and `rarity: "Mythic"`
- **WHEN** it is read
- **THEN** its progression is `Mythic:OneBlueStar`

### Requirement: Cost keys map to the served costs

`costs.shards` SHALL be served as `shards`, `costs.mythicShards` as `mythicShards`, and `costs.heroAscensionOrb{Rarity}` as `orbs` with `orbRarity` `{Rarity}`. An absent key SHALL be served as 0 (or a null `orbRarity`).

#### Scenario: An ascension step with orbs

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** `Legendary:OneBlueStar` costs 500 shards, 0 mythic shards and 20 Legendary orbs

### Requirement: The first step is free

The first row's game cost is the unlock cost, which `unlock-shard-costs` serves, so the first record SHALL have zero shards, mythic shards and orbs, and a null orb rarity.

#### Scenario: Common:None costs nothing

- **GIVEN** the first datamine row `{ stars: 0, rarity: "Common", costs: { shards: 40 } }`
- **WHEN** it is read
- **THEN** the record is `Common:None` with every cost 0

### Requirement: An unexpected ladder fails startup

Loading SHALL fail, naming the dataset and the row, when a row has `stars` outside 0 to 14, more than one `heroAscensionOrb*` key, or any other cost key. Validation SHALL fail when the progressions are not exactly the planner's progression order, when a cost is negative, or when orbs and orb rarity disagree.

#### Scenario: An unknown cost key

- **GIVEN** a non-first row whose costs hold `hreShards`
- **WHEN** the catalog loads
- **THEN** loading fails naming `ascension-costs` and the row

#### Scenario: A dropped step

- **GIVEN** a ladder without `Common:None`
- **WHEN** it is validated
- **THEN** validation fails with `UnexpectedProgressions`
