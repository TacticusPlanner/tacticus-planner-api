## ADDED Requirements

### Requirement: Characters are read from the pinned datamine

Every `gameconfig/units/lineup.json` unit without the `MachineOfWar` trait SHALL be served as a character, grouped under `units-<faction>` (lowercased `FactionId`) and ordered by id. The catalog SHALL keep no hand-kept unit files.

#### Scenario: Same characters as before

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** the 119 characters equal the ones the retired `units-*.json` files served, and the `characters` hash is unchanged

### Requirement: Character stats and weapons map from the lineup entry

`name`, `initialRarity`, `movement` SHALL come from `name`, `BaseRarity`, `Movement`. `health`, `damage`, `armour` SHALL come from `stats.Health`, `stats.Damage`, `stats.FixedArmor`. Melee and ranged SHALL come from `weapons[0]` and `weapons[1]` (`DamageProfile`, `hits`, `Range`). `traits` SHALL be the game's without `Hero`, and `equipmentSlots` SHALL be `itemSlots`.

#### Scenario: Hero trait dropped

- **GIVEN** a character with traits `["Hero", "Psyker"]`
- **WHEN** it is read
- **THEN** its `traits` are `["Psyker"]`

### Requirement: Character abilities and rank-ups map from the lineup entry

Ability ids SHALL keep game order. Each damage list SHALL be the distinct `damageProfile` / `damageProfile_N` constants of those abilities in `abilities.json`, sorted ordinally. `rankUpUpgrades` SHALL have one row per `upgrades` entry, ranked `Stone1`, `Stone2`, … in order.

#### Scenario: Damage profiles are sorted

- **GIVEN** a character whose active ability has `damageProfile` `Power` and `damageProfile_2` `Bolter`
- **WHEN** it is read
- **THEN** its `activeAbilityDamage` is `["Bolter", "Power"]`

#### Scenario: Rank-up rows

- **GIVEN** a character with two `upgrades` rows
- **WHEN** it is read
- **THEN** its `rankUpUpgrades` are `Stone1` and `Stone2`, each with that row's six ids

### Requirement: An unreadable character fails startup

Loading SHALL fail, naming the unit, when its first weapon has a range, it has more than two weapons, it names an ability `abilities.json` does not define, or it has more rank-up rows than there are ranks.

#### Scenario: A ranged-only weapon list

- **GIVEN** a character whose only weapon has a `Range`
- **WHEN** the catalog loads
- **THEN** loading fails naming the character
