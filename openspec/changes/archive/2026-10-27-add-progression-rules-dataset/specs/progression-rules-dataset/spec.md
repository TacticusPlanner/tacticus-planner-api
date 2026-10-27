## ADDED Requirements

### Requirement: Progression rules are built from the pinned datamine files

The served `progression-rules` dataset SHALL be a one-element list built from the datamine copies
`gameconfig/units/xpLevels.json`, `gameconfig/consumables.json`, `gameconfig/units/upgradeSlots.json`,
`gameconfig/units/heroProgressionSteps.json` and `gameconfig/units/heroProgressionStepsMoW.json`.

#### Scenario: The shipped XP curve

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** `xpLevels` holds 64 totals from 25 to 7360200

### Requirement: XP books are served one per rarity

`xpBooks` SHALL hold one record per rarity in rarity order, each with `id`, `rarity`, `xpIncrease` and `gold`.

#### Scenario: The Legendary book

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** the Legendary book is `xpLegendary`, giving 12500 XP for 500 gold

### Requirement: Upgrade slot XP levels are keyed by rank

`upgradeSlots` SHALL hold one record per rank from Stone1 to Adamantine2, each with its `rank` and the six
`xpLevelRequirements` of its upgrade slots in the game's order.

#### Scenario: Diamond 2

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** the Diamond2 row is `[47, 48, 47, 49, 47, 50]`

### Requirement: Each ascension step carries its caps and multipliers

`heroProgressionSteps` SHALL hold one record per hero ascension step with `step`, `progression`, `rarity`, `stars`,
`maxXpLevel`, `maxRank` (a rank id), `unitStatMultiplierPct`, `abilityStatMultiplierPct` and `hreShards` (null when the
game lists none). `mowProgressionSteps` SHALL hold one record per MoW step with `step`, `progression`, `rarity`,
`stars`, `maxAbilityLevel`, `abilityStatMultiplierPct` and `mythicAbilityStatMultiplierPct` (null below Mythic).

#### Scenario: The first hero step

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** the first hero step is `Common:None` with max XP level 8, max rank Iron1 and 25 HRE shards

### Requirement: Misshapen tables fail startup

The catalog loader SHALL fail startup, naming `progression-rules`, when the XP curve is empty, not positive or not
strictly increasing; the XP books are not one per rarity with an id and positive XP and gold; the slot rows are not
20 rows of six positive levels; a hero step's max rank is not a known rank; or either ladder does not walk exactly the
catalog progression order.

#### Scenario: A dropped ladder step

- **GIVEN** a hero ladder missing its first step
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `progression-rules` and `heroProgressionSteps`
