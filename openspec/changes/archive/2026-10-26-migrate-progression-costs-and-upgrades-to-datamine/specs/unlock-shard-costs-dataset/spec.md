## ADDED Requirements

### Requirement: Unlock costs are built from the pinned datamine file

The served `unlock-shard-costs` dataset SHALL be built from the datamine copy `gameconfig/units/heroConversion.json` (starting rarity to `shards`). It SHALL have one record per key, ordered Common, Uncommon, Rare, Epic, Legendary, Mythic. The catalog SHALL keep no hand-kept copy.

#### Scenario: The game's unlock costs

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds Common 40, Uncommon 80, Rare 130, Epic 250 and Legendary 500, in that order

### Requirement: No Mythic unlock record

The catalog SHALL NOT add records the game does not define. At `v1.43.157.2989` the game has no Mythic unlock cost and no character starts at Mythic, so no Mythic record is served (V1's 1400 is dropped).

#### Scenario: Mythic is absent

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** no record has rarity `Mythic`

### Requirement: An unknown rarity fails startup

Loading SHALL fail, naming the dataset and the keys, when the file has a key that is not a known rarity.

#### Scenario: An unknown rarity

- **GIVEN** a file with key `Relic`
- **WHEN** the catalog loads
- **THEN** loading fails naming `unlock-shard-costs` and `Relic`
