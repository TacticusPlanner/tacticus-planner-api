# Design

## Context

Every value comes from a datamine file the catalog already pins and reads, so no allowlist change. At
`v1.43.157.2989` all 14 legendary events share one token bank (6 start, 12 max, 1 per 10800 s, 1 refill a day, 3 runs);
all 5 quest events share theirs (6 max, 1 per 7200 s, 50 gems a refill; 100 points a star, up to 100 for a lightning
victory). Companion apps change: roadmap step 7 apps PRs.

## Decisions

### D1. Per event, not global

The game sets these per event, so the catalog serves them per event even though every event agrees today. A future
event with different rules needs no shape change.

### D2. Planner names for the bank, game names for scoring

The game's "stamina" is the planner's "tokens", so the bank is served as `tokens` with unit-bearing names
(`regenSeconds`). `scorePoints` is passed through with the game's names, like `hre`'s.

### D3. One Oh So Close record

`lres` and `hre` ship the same `ohSoCloseOffer` shape. The served record is shared (`GameCatalogOhSoCloseOffer`); the
JSON of `hre` is unchanged, so its hash does not move.

### D4. Campaign number from the id

Event campaigns 3 to 6 share the battle `campaignId` 1031/1032, so it cannot number them. The number is the campaign
id's trailing digits for every kind; for storylines the battles' `campaignId` must agree, which catches a renumbering.

### D5. `maxRuns` is not derived run state

The run-schedule rule keeps clock-derived state off `lres`. `maxRuns` is a game constant, so the rule and its guard
test name it as the one exception.
