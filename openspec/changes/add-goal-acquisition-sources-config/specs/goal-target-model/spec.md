## ADDED Requirements

### Requirement: Goal farming config carries an acquisition-source set

`GoalConfig` SHALL expose `acquisitionSources`: an ordered list of entries, each with a
`kind` string and an `ids` string list. `kind` SHALL be validated against a server-owned
allow-list — `Campaign`, `Onslaught`, `Shop` — that MAY be extended with further values
without a wire-contract break. A request whose `acquisitionSources` contains an entry with a
`kind` outside the allow-list SHALL be rejected.

#### Scenario: Allow-listed kinds are accepted

- **WHEN** a Character Ascension goal is created with `acquisitionSources` of
  `[{ kind: "Campaign", ids: [<valid battle id>] }, { kind: "Onslaught", ids: [] }]`
- **THEN** creation succeeds and the goal round-trips with those two entries in order

#### Scenario: Unknown kind is rejected

- **WHEN** a goal request contains an `acquisitionSources` entry with `kind` `"Auction"`
- **THEN** validation rejects the request as unsupported

### Requirement: Acquisition-source ids are validated per kind

For a `Campaign` entry, every id SHALL be a shard-farm battle id available to the target
character (the same regular/mythic battle-id sets already used to validate shard farming);
an unknown or wrong-type battle id SHALL be rejected. For a `Shop` entry, every id SHALL
match the `<shopId>:<rewardType>` shape and reference a shop the server knows. An
`Onslaught` entry SHALL carry an empty `ids` list; a non-empty one SHALL be rejected.

#### Scenario: Invalid campaign battle id is rejected

- **WHEN** a `Campaign` entry lists a battle id that is not one of the target character's
  shard-farm battles
- **THEN** validation rejects the request

#### Scenario: Malformed shop offer id is rejected

- **WHEN** a `Shop` entry lists an id that does not match `<shopId>:<rewardType>` or names an
  unknown shop
- **THEN** validation rejects the request

#### Scenario: Onslaught entry with ids is rejected

- **WHEN** an `Onslaught` entry has a non-empty `ids` list
- **THEN** validation rejects the request

### Requirement: Source kinds are gated by entity and goal type

`Onslaught` entries SHALL be accepted only for Character Ascension goals. `Onslaught` and
`Shop` entries SHALL be rejected for Machine-of-War goals. `Campaign` and `Shop` entries
SHALL be accepted for Character Unlock and Character Ascension goals.

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

### Requirement: The single-select ascension farming source model is removed

The goal API and generated OpenAPI SHALL NOT expose `ascensionFarming`, the
`AscensionFarmingSource` enum, or its `shardBattleIds` / `mythicShardBattleIds` fields. The
source choice SHALL be expressed only through `acquisitionSources`.

#### Scenario: OpenAPI no longer describes the old model

- **WHEN** OpenAPI is generated
- **THEN** no schema references `AscensionFarmingSource` or an `ascensionFarming` config
  property

#### Scenario: A request using the old field does not set a source

- **WHEN** a goal request carries a legacy `ascensionFarming` object
- **THEN** it does not influence the persisted config; only `acquisitionSources` is honoured

### Requirement: FarmingLocationIds retains its upgrade-node role

`GoalConfig.farmingLocationIds` SHALL continue to be accepted and returned as the Rank/Ability
upgrade-node farming override. Only its Unlock/Ascension shard-node role is replaced by
`acquisitionSources`.

#### Scenario: Rank goal farming override is unchanged

- **WHEN** a Rank goal is created with `farmingLocationIds` set
- **THEN** the goal round-trips with the same `farmingLocationIds` and no `acquisitionSources`
  is required

### Requirement: Existing goals migrate to an equivalent acquisition-source set

A migration SHALL rewrite each persisted goal's `config` so that its shard-source choice is
expressed as `acquisitionSources`: a `Campaign` entry SHALL carry the union of the old
`ascensionFarming.shardBattleIds` and `mythicShardBattleIds`, or an Unlock goal's old
`farmingLocationIds`; an `Onslaught` entry SHALL be added when the old
`ascensionFarming.source` was `Onslaught` or `Both`. The old `ascensionFarming` object SHALL
be removed and an Unlock goal's shard `farmingLocationIds` SHALL be cleared. A goal with
neither old field SHALL become `[{ kind: "Campaign", ids: [] }]`.

#### Scenario: Both maps to campaign plus onslaught

- **GIVEN** an Ascension goal stored with `ascensionFarming.source = "Both"` and two
  `shardBattleIds`
- **WHEN** the migration applies
- **THEN** its `acquisitionSources` is `[{ kind: "Campaign", ids: [those two ids] },
  { kind: "Onslaught", ids: [] }]` and `ascensionFarming` is gone

#### Scenario: Campaign-only Ascension goal keeps its battle ids

- **GIVEN** an Ascension goal stored with `ascensionFarming.source = "Campaign"` and one
  regular and one mythic battle id
- **WHEN** the migration applies
- **THEN** its `acquisitionSources` is `[{ kind: "Campaign", ids: [both ids] }]`

#### Scenario: Unlock goal shard locations move into a campaign entry

- **GIVEN** an Unlock goal stored with `farmingLocationIds` of two shard battle ids
- **WHEN** the migration applies
- **THEN** its `acquisitionSources` is `[{ kind: "Campaign", ids: [those two ids] }]` and its
  `farmingLocationIds` is cleared

#### Scenario: Goal with no prior source becomes unrestricted campaign

- **GIVEN** an Ascension goal stored with no `ascensionFarming` and no `farmingLocationIds`
- **WHEN** the migration applies
- **THEN** its `acquisitionSources` is `[{ kind: "Campaign", ids: [] }]`

#### Scenario: Rank/Ability farming override is untouched by the migration

- **GIVEN** a Rank goal stored with `farmingLocationIds`
- **WHEN** the migration applies
- **THEN** its `farmingLocationIds` is unchanged and it gains no `acquisitionSources`

### Requirement: Acquisition sources can be edited on an existing goal

`UpdateGoalRequest` SHALL allow replacing an Unlock or Ascension goal's `acquisitionSources`,
subject to the same per-kind and entity/goal-type validation as creation. Clearing the field
SHALL reset the goal to unrestricted campaign farming.

#### Scenario: Adding a shop source to an existing goal

- **WHEN** an existing Character Ascension goal is updated with `acquisitionSources` that adds
  a valid `Shop` entry
- **THEN** the update succeeds and a subsequent read returns the shop entry

#### Scenario: Invalid update is rejected without changing the goal

- **WHEN** an update sends an `acquisitionSources` entry with an unknown `kind`
- **THEN** the update is rejected and the goal's stored sources are unchanged
