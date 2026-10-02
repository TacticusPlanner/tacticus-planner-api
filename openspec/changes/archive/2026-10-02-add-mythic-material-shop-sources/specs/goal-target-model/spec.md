## MODIFIED Requirements

### Requirement: Acquisition-source ids are validated per kind

For a `Campaign` entry, every id SHALL be a shard-farm battle id available to the target
character (the same regular/mythic battle-id sets already used to validate shard farming);
an unknown or wrong-type battle id SHALL be rejected. For a `Shop` entry, every id SHALL
match the `<shopId>:<rewardType>` shape and reference a shop the server knows, where
`<rewardType>` is either a character-shard reward (`shards_<unitId>` /
`mythicShards_<unitId>`) or one of the four Mythic upgrade-material reward ids `upgHpM001`,
`upgHpM002`, `upgHpM003`, `upgHpM004`. Any other reward type SHALL be rejected. A `Shop` entry
MAY carry an empty `ids` list. An `Onslaught` entry SHALL carry an empty `ids` list; a
non-empty one SHALL be rejected.

#### Scenario: Invalid campaign battle id is rejected

- **WHEN** a `Campaign` entry lists a battle id that is not one of the target character's
  shard-farm battles
- **THEN** validation rejects the request

#### Scenario: Malformed shop offer id is rejected

- **WHEN** a `Shop` entry lists an id that does not match `<shopId>:<rewardType>` or names an
  unknown shop
- **THEN** validation rejects the request

#### Scenario: Mythic material shop offer id is well-formed

- **WHEN** a `Shop` entry lists `guild:upgHpM004`
- **THEN** the id passes the shape check

#### Scenario: Other upgrade-material reward type is rejected

- **WHEN** a `Shop` entry lists `guild:upgDmgL202` (a Legendary material, not one of the four
  Mythic materials)
- **THEN** validation rejects the request

#### Scenario: Onslaught entry with ids is rejected

- **WHEN** an `Onslaught` entry has a non-empty `ids` list
- **THEN** validation rejects the request

### Requirement: Source kinds are gated by entity and goal type

`Onslaught` entries SHALL be accepted only for Character Ascension goals. `Campaign` and
`Shop` entries SHALL be accepted for Character Unlock and Character Ascension goals, and their
`Shop` ids SHALL be character-shard rewards only; `Onslaught` and `Shop` entries SHALL be
rejected for Machine-of-War Unlock and Ascension goals. `Shop` entries SHALL also be accepted
for Character Rank goals, Character and Machine-of-War Upgrade goals, and Machine-of-War
Ability goals, and their `Shop` ids SHALL be Mythic upgrade-material rewards only. `Campaign`
and `Onslaught` entries SHALL be rejected for Rank, Upgrade, and Ability goals (their campaign
upgrade-node override remains `farmingLocationIds`). Any acquisition-source entry SHALL be
rejected for Character Ability goals. These rules SHALL apply identically on goal creation,
combined goal creation, and goal update.

#### Scenario: Onslaught on a Machine-of-War goal is rejected

- **WHEN** a MoW goal request includes an `{ kind: "Onslaught" }` entry
- **THEN** validation rejects the request

#### Scenario: Onslaught on an Unlock goal is rejected

- **WHEN** a Character Unlock goal request includes an `{ kind: "Onslaught" }` entry
- **THEN** validation rejects the request

#### Scenario: Shop source on a Character Unlock goal is accepted

- **WHEN** a Character Unlock goal request includes a valid `{ kind: "Shop", ids: [...] }`
  entry
- **THEN** creation succeeds and the entry round-trips

#### Scenario: Mythic material shop source on a Character Rank goal is accepted

- **WHEN** a Character Rank goal is created with `acquisitionSources` of
  `[{ kind: "Shop", ids: ["guild:upgHpM004", "crusade:upgHpM004"] }]`
- **THEN** creation succeeds and the goal round-trips with that entry, alongside any
  `farmingLocationIds` it was created with

#### Scenario: Mythic material shop source on a Character Upgrade goal is accepted

- **WHEN** a Character Upgrade goal targeting `upgHpM004` is updated with
  `acquisitionSources` of `[{ kind: "Shop", ids: ["rogue-trader:upgHpM004"] }]`
- **THEN** the update succeeds and the entry round-trips

#### Scenario: Explicit empty shop selection round-trips

- **WHEN** a Character Rank goal is saved with `acquisitionSources` of
  `[{ kind: "Shop", ids: [] }]`
- **THEN** the goal round-trips with exactly that entry, distinct from a goal whose
  `acquisitionSources` is `null`

#### Scenario: Shard offer on a Rank goal is rejected

- **WHEN** a Character Rank goal request includes `{ kind: "Shop", ids: ["guild:shards_ragnar"] }`
- **THEN** validation rejects the request

#### Scenario: Mythic material offer on an Ascension goal is rejected

- **WHEN** a Character Ascension goal request includes `{ kind: "Shop", ids: ["guild:upgHpM004"] }`
- **THEN** validation rejects the request

#### Scenario: Campaign entry on a Rank goal is rejected

- **WHEN** a Character Rank goal request includes `{ kind: "Campaign", ids: [] }`
- **THEN** validation rejects the request

#### Scenario: Mythic material shop source on a Machine-of-War Ability goal is accepted

- **WHEN** a Machine-of-War Ability goal for `ultraDreadnought` is created with
  `[{ kind: "Shop", ids: ["guild:upgHpM004"] }]`
- **THEN** creation succeeds and the entry round-trips

#### Scenario: Shop entry on a Character Ability goal is rejected

- **WHEN** a Character Ability goal request includes a `{ kind: "Shop" }` entry
- **THEN** validation rejects the request

#### Scenario: Shop entry on a Machine-of-War Unlock goal is rejected

- **WHEN** a MoW Unlock goal request includes a `{ kind: "Shop" }` entry
- **THEN** validation rejects the request
