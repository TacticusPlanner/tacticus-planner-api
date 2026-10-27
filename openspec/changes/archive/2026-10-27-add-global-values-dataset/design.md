# Design

## Context

`gameconfig/globalValues.json` is one object of ~200 game-wide settings. The apps need about fifteen of them.
`gameconfig/shop/staminaShopTiers.json` lists the energy shop's product ids in purchase order; each is a
`products.json` product with `rewards` (`stamina:N`, `raidTicket:N`) and a gem `cost`. Companion apps change: roadmap
step 7 "Use `global-values`".

## Decisions

### D1. Bind only what a consumer needs, under the game's names

Serving all ~200 values would make every unrelated game tweak move the hash. The record binds the values the apps
read, keeps the game's names and units (seconds, percentages), and leaves derivations (288 energy a day, 1.5 Onslaught
runs a day) to the apps. A new field is a `SchemaVersion` bump.

### D2. One-element list

Like `unit-power`, the single record is served as a one-element list, so every dataset envelope holds an array.

### D3. Trait variables as shipped

`traitVariables` mixes numbers and strings (`putridExplosionFactionId`, `terminatorArmourExcludedDmgProfiles`). It is
served as the game's map, sorted by key, without typing each entry; the raid-boss text generator reads keys it knows.

### D4. Resolve the energy shop ladder

Product ids alone are useless to the apps, so each tier is resolved to its energy, raid tickets and gem cost. The game
has two first-tier products (`product_stamina_00`, `product_stamina_00_2`); both are served in game order, and the apps
decide which applies. The "+90 energy for ads" tier is not in the game config and stays curated in the apps.

### D5. Fail on unmappable values

A missing or zero stamina value would make the apps divide by zero; a tier with an unknown reward would silently drop
it. Both fail startup naming the field or product.
