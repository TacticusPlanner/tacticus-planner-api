# Design

## Context

The game's progression tables are spread over five files. `xpLevels.json` is 64 cumulative XP totals (level 2 to 65).
`consumables.json` `xpBooks` lists six books. `upgradeSlots.json` `xpLevelRequirements` has 20 rows of six XP levels,
one per rank the upgrades are applied at. `heroProgressionSteps.json` and `heroProgressionStepsMoW.json` have 20 steps
each with caps and multipliers next to the costs `ascension-costs` and `unit-power` already serve. Companion apps
change: roadmap step 7 "XP and levels" and "Caps and stars" from `progression-rules`.

## Decisions

### D1. One record, game names

The tables are small and read together by goal planning, so they are served as one record (a one-element list, like
`unit-power`). Field names are the game's; the only translation is rank indices to the catalog's rank ids (`UnitRank`
names, as `rankUpUpgrades` already uses) and `{stars, rarity}` to the `{Rarity}:{Stars}` progression string.

### D2. No duplication with existing datasets

Ascension costs stay in `ascension-costs` and power multipliers (`abilityPowerMultiplier`) in `unit-power`. This record
carries only what no dataset serves: caps, stat multipliers and `hreShards`.

### D3. XP curve as the game ships it

`xpLevels[i]` is the total XP to reach level i + 2. The apps' table prepends level 1 (0 XP); serving the game's array
keeps the 1:1 trail to `raw/`, and the apps add the leading 0.

### D4. Serve the game's max rank

The game caps Mythic at rank index 20 (Adamantine3). The apps and the API `GameDomain` cap at Adamantine2 because
nothing in the game reaches A3 yet. The dataset serves the game's value; whether the planner follows it is the apps
change's call.

### D5. Structural checks at load

Every consumer indexes these tables by position (level, rank, step), so a reshaped file must fail startup rather than
shift values: the XP curve must increase, rank rows must be 20 × 6, and both ladders must walk
`ProgressionRules.ProgressionOrder`.
