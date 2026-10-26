# onslaught-rewards-dataset Specification

## Purpose
Provides the backend game catalog's Onslaught reward reference data — per sector tier, the hero shard ranges for each rarity step, the mythic shard range and the ability badges earned across its rounds — derived from the pinned datamine so a client can plan Onslaught income without hand-kept tables.

## Requirements

### Requirement: Onslaught rewards are read from the pinned datamine

The `onslaught-rewards` dataset SHALL be built from the pinned datamine copy `gameconfig/battles/onslaught.json`, one record per entry of `tiers`, in tier order. Tier `t` SHALL be served as `id` `<sector>-<n>`, `sector` the `t`-th group of three in Stone, Iron, Bronze, Silver, Gold, Diamond, Adamantine, and `tier` `n` (1 to 3) within it.

#### Scenario: Tier ids

- **WHEN** the dataset is served from datamine `v1.43.157.2989`
- **THEN** it holds 21 records from `Stone-1` to `Adamantine-3`

### Requirement: Onslaught shard ranges come from the hero reward rows

`regular` SHALL be the `shards_[heroId]:min-max` ranges of the tier's `honorYourHeroesRewards` rows for hero progression index 2, 5, 8, 11 and 14 (the top Common, Uncommon, Rare, Epic and pre-blue-star Legendary step), and `mythic` the `mythicShards_[heroId]` range for index 19, each from the first row whose `maxProgressionIndex` reaches the index.

#### Scenario: Adamantine 3

- **WHEN** the dataset is served from datamine `v1.43.157.2989`
- **THEN** `Adamantine-3` has `regular` 3–4, 4–5, 6–7, 10–11, 18–22, `mythic` 2–3 and badges Common 1, Uncommon 2, Rare 2, Epic 2, Legendary 2, Mythic 1

### Requirement: Onslaught badges are summed from the tier waves

`badges` SHALL be the `tierWaves` ability-badge rewards of the tier, one per round, summed per rarity in rarity order.

#### Scenario: Bronze 1 takes the game's badges

- **WHEN** the dataset is served from datamine `v1.43.157.2989`
- **THEN** `Bronze-1` has badges Common 1, Uncommon 3, Rare 2 (V1's hand-kept file had Common 2), and the other 20 tiers equal V1's

### Requirement: An unreadable onslaught tier fails startup

Loading SHALL fail, naming `onslaught-rewards` and the tier, when a tier is outside the seven sectors, a needed shard reward row or range is missing, a round reward is not an ability badge, or the waves of one round give different badges.

#### Scenario: Waves disagree

- **GIVEN** two waves of tier 1 round 1 that give a Common and a Rare badge
- **WHEN** the catalog loads
- **THEN** loading fails naming `onslaught-rewards` and round 1
