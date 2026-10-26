# character-ability-costs-dataset Specification

## Purpose
Provides the game catalog's character ability cost ladder, the gold and ability badges needed to raise a character's ability to each level, so clients can compute what a Character Ability goal still costs without embedding a cost table of their own.

## Requirements

### Requirement: The catalog serves a `character-ability-costs` dataset

The game catalog SHALL serve a dataset keyed `character-ability-costs` as a plain array, one record per ability level a character ability can be raised to, ordered by ascending `level`, at `GET /api/v1/game-catalog/character-ability-costs` without requiring authentication, consistent with the rest of the catalog. It SHALL be built from the pinned datamine copy `gameconfig/units/abilityUpgradeCosts.json`; the catalog SHALL keep no hand-kept copy, and the raw file SHALL NOT be served directly. The dataset is shared by every character and every ability track, not per character.

#### Scenario: Dataset is served without authentication

- **WHEN** a client requests the `character-ability-costs` endpoint
- **THEN** the ladder array is returned without requiring authentication, wrapped in the same dataset envelope as the other served datasets

#### Scenario: Dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `character-ability-costs`, and the manifest snapshot test covers it

#### Scenario: The raw source has no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving the raw `abilityUpgradeCosts.json` directly

### Requirement: Each record is keyed by the level it raises an ability to

Each served record SHALL carry exactly these fields and no others: `level` (integer, the ability level the record's cost raises an ability to; the first record is `2` because level 1 is the free starting level), `gold` (integer, the row's `gold`, at least 0), `badges` (object with `rarity`, one of `Common`, `Uncommon`, `Rare`, `Epic`, `Legendary`, `Mythic`, and `amount`, a positive integer count of ability badges of that rarity). Row N (1-based) of the datamine file is the cost from level N to N + 1, so the served `level` SHALL be N + 1. The records SHALL cover every level from 2 to the maximum ability level with no gaps or duplicates (59 records, levels 2 to 60, at `v1.43.157.2989`).

#### Scenario: Levels are keyed by the level raised to

- **GIVEN** the first datamine row `{ gold: 25, abilityTokenCommon: 1 }`
- **WHEN** the dataset is served
- **THEN** the record has `level: 2`, `gold: 25`, and `badges: { rarity: "Common", amount: 1 }`

#### Scenario: The ladder has no gaps

- **WHEN** the dataset is served
- **THEN** its records' levels are exactly 2 through 60, each once, in ascending order

#### Scenario: Game values replace V1's

- **WHEN** the dataset is served at `v1.43.157.2989`
- **THEN** levels 52 and 53 cost 130000 and 132500 gold (V1 had 127500 and 130000), and every other record equals V1's

### Requirement: Records carry no display text or icon

Consistent with every served catalog dataset, a `character-ability-costs` record SHALL NOT include a badge display name, icon, icon id, or any presentational string. Only the structured fields above are served; the client resolves display from the rarity id.

#### Scenario: Served payload shape

- **WHEN** a record is served
- **THEN** it contains only `level`, `gold` and `badges` (`rarity`, `amount`), and no display-text or icon field

### Requirement: The raw ladder is validated at load

The catalog loader SHALL fail startup, as it does for other datasets, when the datamine ladder is missing or empty, a row has no `gold` or negative gold, a row does not have exactly one `abilityToken{Rarity}` key besides `gold`, the rarity is not a known rarity, or the badge amount is not positive. A missing, empty or invalid dataset SHALL be distinguishable from a valid dataset by startup failing rather than serving a partial ladder.

#### Scenario: A gap in the raw levels

- **GIVEN** a cost ladder whose `lvl` values skip from 4 to 6
- **WHEN** it is validated
- **THEN** validation fails with a `NonConsecutiveLevel` error naming the dataset

#### Scenario: A row with two token keys

- **GIVEN** a datamine row `{ gold: 25, abilityTokenCommon: 1, abilityTokenRare: 1 }`
- **WHEN** the catalog loads
- **THEN** loading fails with an `InvalidCost` error naming the dataset

#### Scenario: A valid ladder loads

- **GIVEN** the pinned datamine copy
- **WHEN** the catalog loads
- **THEN** loading succeeds and the dataset is served

### Requirement: Badge rarity comes from the game's token key

The served `badges.rarity` SHALL be the suffix of the row's `abilityToken{Rarity}` key, and `badges.amount` its value. The catalog SHALL NOT derive the rarity from the level.

#### Scenario: Token key to rarity

- **GIVEN** a datamine row `{ gold: 25, abilityTokenEpic: 3 }`
- **WHEN** it is read
- **THEN** its badges are `{ rarity: "Epic", amount: 3 }`

#### Scenario: The 1.43 rarities match V1's bands

- **WHEN** the dataset is served at `v1.43.157.2989`
- **THEN** level 8 has rarity `Common`, level 9 `Uncommon`, level 18 `Rare`, level 27 `Epic`, level 36 `Legendary`, level 50 `Legendary`, and level 51 `Mythic`
