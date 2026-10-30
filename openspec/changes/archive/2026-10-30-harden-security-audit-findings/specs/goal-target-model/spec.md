## MODIFIED Requirements

### Requirement: FarmingLocationIds retains its upgrade-node role

`GoalConfig.farmingLocationIds` SHALL be accepted and returned as the Rank/Ability upgrade-node farming override. Unlock and Ascension shard sources SHALL be expressed only through `acquisitionSources`.

On goal creation and on the details edit, a non-empty `farmingLocationIds` SHALL be validated against the Game Catalog by goal type:

- On Rank, Ability and Upgrade goals, every id SHALL be a campaign battle id.
- On an Unlock goal, every id SHALL be one of the character's regular shard battles.
- An Ascension goal SHALL take none.

A goal SHALL carry at most 100 ids. A violation SHALL be a 400 that changes nothing.

#### Scenario: Rank goal farming override round-trips

- **WHEN** a Rank goal is created with `farmingLocationIds` set
- **THEN** the goal round-trips with the same `farmingLocationIds` and no `acquisitionSources`
  is required

#### Scenario: Unknown campaign battle

- **WHEN** a Rank goal is created with a `farmingLocationIds` entry that is not a catalog campaign battle
- **THEN** the response is 400 and no goal is created

#### Scenario: Farming locations on an Ascension goal

- **WHEN** an Ascension goal is created or edited with a non-empty `farmingLocationIds`
- **THEN** the response is 400 directing the caller to acquisition sources

#### Scenario: Too many farming locations

- **WHEN** a goal is created or edited with 101 `farmingLocationIds`
- **THEN** the response is 400 `validation`
