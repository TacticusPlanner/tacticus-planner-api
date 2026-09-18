## MODIFIED Requirements

### Requirement: Simultaneous farm rewards have one combined expected yield
The game catalog SHALL represent rewards for the same resource and battle as one farm location. When a battle awards one or more guaranteed or probabilistic copies of that resource simultaneously, the location's effective rate SHALL equal the number of guaranteed occurrences plus every probabilistic expected rate. A single probabilistic occurrence SHALL retain its chance ID, numerator, and denominator. Any consolidated location SHALL set all three fields to null because they cannot describe the combined yield. Every farm location SHALL additionally carry `expectedGold`: the average of its battle's guaranteed gold reward (`(min + max) / 2`), or null when that battle awards no guaranteed gold. `expectedGold` is a property of the battle, so every location sharing that battle (including two consolidated locations for two different resources dropped by the same battle) SHALL report the same value.

#### Scenario: Elite character shard has a guaranteed shard and bonus chance
- **GIVEN** an elite battle awards one guaranteed character shard and a `shard_elite` bonus with effective rate `0.079`
- **WHEN** character shard farm locations are denormalized
- **THEN** the catalog contains one location for that character and battle with `guaranteed` set, an effective rate of `1.079`, and null chance ID, numerator, and denominator

#### Scenario: A reward occurs only once in a battle
- **GIVEN** a battle has only one guaranteed or probabilistic occurrence of a resource
- **WHEN** farm locations are denormalized
- **THEN** its existing guaranteed and drop-chance semantics remain unchanged

#### Scenario: Two probabilistic occurrences use different chance definitions
- **GIVEN** a battle awards the same resource through two probabilistic occurrences with different chance IDs
- **WHEN** farm locations are denormalized
- **THEN** the catalog contains one location whose effective rate is the sum of both occurrences and whose chance ID, numerator, and denominator are null

#### Scenario: Two battles tie on efficiency but differ on gold
- **GIVEN** battle `FoCE13` guarantees gold `109`-`165` and battle `SHME19` guarantees gold `123`-`180`, and both battles also guarantee the same material
- **WHEN** farm locations for that material are denormalized
- **THEN** the `FoCE13` location reports `expectedGold` `137` and the `SHME19` location reports `expectedGold` `151.5`

#### Scenario: A battle guarantees no gold
- **GIVEN** a battle's guaranteed rewards contain no `gold` entry
- **WHEN** farm locations for that battle are denormalized
- **THEN** every location built from that battle reports `expectedGold` as null
