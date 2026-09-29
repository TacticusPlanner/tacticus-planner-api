# character-ability-costs-dataset Specification

## Purpose
Provides the game catalog's character ability cost ladder, the gold and ability badges needed to raise a character's ability to each level, so clients can compute what a Character Ability goal still costs without embedding a cost table of their own.

## Requirements

### Requirement: The catalog serves a `character-ability-costs` dataset

The game catalog SHALL serve a dataset keyed `character-ability-costs` as a plain array, one record per ability level a character ability can be raised to, ordered by ascending `level`, at `GET /api/v1/game-catalog/character-ability-costs` without requiring authentication, consistent with the rest of the catalog. It SHALL be built from one authored raw source file, `character-ability-costs.json`, whose values are ported unchanged from V1's `characters-lvl-up-abilities.json`; the raw file SHALL NOT be served directly. The dataset is shared by every character and every ability track, not per character.

#### Scenario: Dataset is served without authentication

- **WHEN** a client requests the `character-ability-costs` endpoint
- **THEN** the ladder array is returned without requiring authentication, wrapped in the same dataset envelope as the other served datasets

#### Scenario: Dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `character-ability-costs`, and the manifest snapshot test covers it

#### Scenario: The raw source has no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving the raw `character-ability-costs.json` directly

### Requirement: Each record is keyed by the level it raises an ability to

Each served record SHALL carry exactly these fields and no others: `level` (integer, the ability level the record's cost raises an ability to; the first record is `2` because level 1 is the free starting level), `gold` (integer, gold cost, at least 0), `badges` (object with `rarity`, one of `Common`, `Uncommon`, `Rare`, `Epic`, `Legendary`, `Mythic`, and `amount`, a positive integer count of ability badges of that rarity). The served `level` SHALL equal the raw entry's `lvl` plus 1, since a raw `lvl` N is the cost to go from level N to level N + 1. The records SHALL cover every level from 2 to the maximum ability level with no gaps or duplicates, matching V1's table in length (59 records, levels 2 to 60).

#### Scenario: Levels are keyed by the level raised to

- **GIVEN** the raw entry `lvl: 1, gold: 25, badges: 1`
- **WHEN** the dataset is served
- **THEN** the record has `level: 2`, `gold: 25`, and `badges.amount: 1`

#### Scenario: The ladder has no gaps

- **WHEN** the dataset is served
- **THEN** its records' levels are exactly 2 through 60, each once, in ascending order

### Requirement: Badge rarity is derived from the level raised to

The served `badges.rarity` SHALL be derived at load time from the record's `level`, using V1's bands: levels 2 to 8 are `Common`, 9 to 17 `Uncommon`, 18 to 26 `Rare`, 27 to 35 `Epic`, 36 to 50 `Legendary`, and 51 and above `Mythic`. The raw source SHALL NOT need to store a rarity. The client SHALL receive the rarity as a served value and SHALL NOT need to reproduce the bands.

#### Scenario: Rarity band boundaries

- **WHEN** the dataset is served
- **THEN** level 8 has rarity `Common`, level 9 `Uncommon`, level 17 `Uncommon`, level 18 `Rare`, level 35 `Epic`, level 36 `Legendary`, level 50 `Legendary`, and level 51 `Mythic`

### Requirement: Records carry no display text or icon

Consistent with every served catalog dataset, a `character-ability-costs` record SHALL NOT include a badge display name, icon, icon id, or any presentational string. Only the structured fields above are served; the client resolves display from the rarity id.

#### Scenario: Served payload shape

- **WHEN** a record is served
- **THEN** it contains only `level`, `gold` and `badges` (`rarity`, `amount`), and no display-text or icon field

### Requirement: The raw ladder is validated at load

The catalog loader SHALL fail startup, as it does for other datasets, when the raw `character-ability-costs` source is missing, empty, has a non-positive badge amount or negative gold, or has non-consecutive `lvl` values starting from 1. A missing, empty or invalid dataset SHALL be distinguishable from a valid dataset by startup failing rather than serving a partial ladder.

#### Scenario: A gap in the raw levels

- **GIVEN** a raw source whose `lvl` values skip from 4 to 6
- **WHEN** the catalog loads
- **THEN** loading fails with a validation error naming the dataset

#### Scenario: A valid ladder loads

- **GIVEN** the shipped raw source
- **WHEN** the catalog loads
- **THEN** loading succeeds and the dataset is served
