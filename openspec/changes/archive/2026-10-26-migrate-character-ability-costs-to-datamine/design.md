# Design

## Context

`gameconfig/units/abilityUpgradeCosts.json` is a 59-row array, each row `{ gold, abilityToken<Rarity>: n }`. V1's `characters-lvl-up-abilities.json` (ported unchanged as `Data/character-ability-costs.json`) carried `lvl`, `gold` and a bare `badges` count; the catalog derived the badge rarity from the level with bands copied from V1's `CharactersAbilitiesService.getRarityFromLevel`.

A row-by-row comparison at `v1.43.157.2989`: same 59 rows; badge counts identical; the token rarity equals the V1 band on every row; gold identical except rows 51 and 52 (`lvl` 51/52, served levels 52/53), where V1 repeated 127,500 and lagged one row behind the game's 2,500 steps until row 53 caught up at 135,000.

## Decisions

### D1. Row position is the level

The datamine row has no level field. Row N (1-based) is `lvl` N, which keeps the raw record and the `NonConsecutiveLevel` check meaningful for constructed inputs and keeps the served `level = lvl + 1`.

### D2. Rarity from the token key, bands deleted

The token key is game data; the bands were a V1 heuristic. Using the key means a game change to the bands flows through a `REF` bump. Tests pin the 1.43 rarities at the V1 band boundaries so such a change is noticed.

### D3. Unmappable rows fail startup

A row with zero or several token keys, or any other non-`gold` key, would mean the game changed the cost shape. The reader leaves `badges` null and the validator fails startup naming the level, rather than serving a guess.

### D4. Take the game's gold for levels 52 and 53

The datamine is the game's own config, so its values win. The served shape is kept, so this is a content-only hash move under the roadmap rule.
