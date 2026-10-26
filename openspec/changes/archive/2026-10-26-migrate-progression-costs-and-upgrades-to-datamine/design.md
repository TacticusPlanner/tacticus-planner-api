# Design

## Context

At `v1.43.157.2989`:

- `heroProgressionSteps.json` has 20 rows in ladder order. They match the served ascension ladder except row 0, whose `costs.shards` is 40, the Common unlock cost.
- `heroConversion.json` maps Common to Legendary to the same unlock shards V1 served, and has no Mythic key.
- `upgrades.json` has 560 upgrades keyed by id, with a developer `name`. The English display name lives in `languages/en.json` under `Upgrades/{id}_name`, and matches every V1 label, including `Mjød` and `Tännhauser's Bones`, which the developer name spells without accents.

## Decisions

### D1. Several datasets per change, each keeping its shape

The maintainer asked for bigger batches. The per-dataset rule is still respected: each dataset keeps its served shape, and every hash move is explained (no rename, no `SchemaVersion` bump).

### D2. The first ascension step stays free

The served ladder is the cost to reach each step from the previous one. Common:None has no previous step, and its game cost is the unlock, which `unlock-shard-costs` already serves. Reading it as free keeps the dataset identical and avoids counting the unlock twice.

### D3. Drop the Mythic unlock record

The game has no Mythic conversion cost, and no character starts at Mythic, so nothing can look it up. Keeping 1400 would need a curated overlay for a value nobody reads, so the record goes.

### D4. Names come from the English strings

The developer `name` drops accents, so the served `label` and `material` come from `en.json`. A missing string fails startup rather than serving the developer name. The whole `en.json` is copied so later migrations (npcs, characters) can read their names from the same pinned file.

### D5. Keep the per-rarity grouping

`UpgradesByRarity` is grouped under the same eleven `upgrades-{rarity}[-crafted]` keys the deleted files used, each ordered by id. The served order (keys ordinal, then id) is unchanged, so the only upgrades diff is the new record.

### D6. Fail on unknown keys

As with the earlier cost tables, an unknown ascension cost key, an unknown star count, an unknown unlock rarity or an unknown upgrade `statType` fails startup naming the dataset and row, instead of silently dropping data.
