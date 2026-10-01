## ADDED Requirements

### Requirement: Unlock eligibility counts shop shard offers

A Character SHALL be eligible for an Unlock goal when the Game Catalog gives it at least one
campaign shard-farm node OR at least one regular shard offer in any catalog shop (a shop variant
whose `unitId` is the character and whose reward type is `shards_<unitId>`). A mythic-only shop
offer (`mythicShards_<unitId>`) SHALL NOT make a character eligible, since unlocking consumes
regular shards. A character that is already owned SHALL still be rejected. A character with
neither source SHALL be rejected as before.

#### Scenario: Shop-only character is accepted

- **GIVEN** a catalog character with no campaign shard nodes and a `shards_<id>` variant in a shop, not owned by the player
- **WHEN** a Character Unlock goal is created for it
- **THEN** the request is accepted

#### Scenario: Mythic-only shop offer is rejected

- **GIVEN** a catalog character with no campaign shard nodes whose only shop variant is `mythicShards_<id>`
- **WHEN** a Character Unlock goal is created for it
- **THEN** the request is rejected as unavailable

#### Scenario: No source is rejected

- **GIVEN** a catalog character with neither campaign shard nodes nor shop shard variants
- **WHEN** a Character Unlock goal is created for it
- **THEN** the request is rejected as unavailable

#### Scenario: Owned shop-only character is rejected

- **GIVEN** a shop-only catalog character the player already owns
- **WHEN** a Character Unlock goal is created for it
- **THEN** the request is rejected as already unlocked
