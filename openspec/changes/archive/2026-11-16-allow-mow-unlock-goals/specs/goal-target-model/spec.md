## ADDED Requirements

### Requirement: Goal types accept the entity types they apply to

Rank goals SHALL be accepted only for Characters. Unlock, Ascension, Ability and Upgrade goals SHALL be accepted for Characters and Machines of War. An Unlock goal SHALL be rejected when the account already owns the unit. A Character Unlock goal SHALL additionally require a campaign shard node or a regular shard shop offer in the catalog; a Machine-of-War Unlock goal SHALL NOT require one.

#### Scenario: Unowned Machine of War gets an Unlock goal

- **GIVEN** the account does not own the Astra Ordnance Battery
- **WHEN** a combined request creates an Unlock goal and an Ability goal depending on it for that Machine of War
- **THEN** both goals are created and the Ability goal depends on the Unlock goal

#### Scenario: Rank goal for a Machine of War is rejected

- **WHEN** a request creates a Rank goal for a Machine of War
- **THEN** validation rejects the request
