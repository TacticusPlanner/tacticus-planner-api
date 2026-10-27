## ADDED Requirements

### Requirement: Global values are built from the pinned datamine files

The served `global-values` dataset SHALL be a one-element list built from the datamine copies `gameconfig/globalValues.json`
and `gameconfig/shop/staminaShopTiers.json`, with each shop tier resolved in `gameconfig/shop/products.json`.

#### Scenario: The shipped values

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** its record has `maxStamina` 60, `staminaRegenerationTime` 300, `wavesMaxStamina` 3,
  `wavesStaminaRegenerationTime` 57600, `minUnitsNeededForBattle` 3, `maxUnitsPossibleForBattle` 5 and 8 stamina shop
  tiers

### Requirement: The record carries the game's values under the game's names

The record SHALL carry `maxStamina`, `staminaRegenerationTime`, `staminaRegenerationAmount`, `wavesMaxStamina`,
`wavesStaminaRegenerationTime`, `wavesStaminaRegenerationAmount`, `campaignLevelMaxStamina`,
`campaignLevelStaminaRegenerationTime`, `campaignLevelStaminaRegenerationAmount`, `minUnitsNeededForBattle`,
`maxUnitsPossibleForBattle`, `terrifyingBonus`, `battleFatigueFleeChance`, `battleFatigueFleeChanceOverkill` and
`traitVariables` (the game's map, sorted by key), with times in seconds.

#### Scenario: A trait variable

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** `traitVariables.deathToTheFalseEmperorExtraHitChance` is 50

### Requirement: Each energy shop tier is resolved to its rewards and cost

`staminaShopTiers` SHALL list the shop's products in game order, each as `productId`, `stamina`, `raidTickets`
(absent = 0) and `gems`.

#### Scenario: The first tier

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** the first tier is `product_stamina_00` giving 60 stamina and 10 raid tickets for 25 gems

### Requirement: Unmappable values fail startup

The catalog loader SHALL fail startup, naming `global-values` and the field or product, when a stamina or team value is
missing or not positive, the maximum team size is below the minimum, `terrifyingBonus` is missing, a battle-fatigue
chance is outside 0-100, `traitVariables` is missing or empty, the shop has no tiers, or a tier is not a
`products.json` product giving one `stamina:N` (and at most one `raidTicket:N`) for a gem cost.

#### Scenario: An unknown tier reward

- **GIVEN** a tier product whose rewards include `gems:5`
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `global-values` and the reward
