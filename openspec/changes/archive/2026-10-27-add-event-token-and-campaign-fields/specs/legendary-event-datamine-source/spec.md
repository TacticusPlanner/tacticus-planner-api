## ADDED Requirements

### Requirement: Events serve the game's token bank, run count and Oh So Close offer

Every record of the served `lres` dataset SHALL carry, read from the event's `liveEvents/legendaryHeroEvent.json` entry:

```
tokens: {              // the token bank (the game's stamina)
  start: int,          // startStamina
  max: int,            // maxStamina
  regenSeconds: int,   // staminaRegenerationTime
  regenAmount: int,    // staminaRegenerationAmount
  dailyRefreshes: int  // maxDailyStaminaRefreshAttempts
},
maxRuns: int,          // heroEventMaxRuns
ohSoCloseOffer: { unlock: { base: int, multiplier: int }, ascension: { base: int, multiplier: int } }
```

`ohSoCloseOffer` has the same shape as on `hre`.

#### Scenario: Shipped token bank

- **WHEN** `lres` is served from datamine `v1.43.157.2989`
- **THEN** every event has `tokens` `{ start: 6, max: 12, regenSeconds: 10800, regenAmount: 1, dailyRefreshes: 1 }` and `maxRuns` 3

### Requirement: Events without a whole token bank, run count or offer fail startup

Catalog load SHALL fail, naming `lres` and the event unit, when `maxStamina`, `staminaRegenerationTime`, `staminaRegenerationAmount` or `heroEventMaxRuns` is missing or not positive, `startStamina` is missing, negative or above `maxStamina`, `maxDailyStaminaRefreshAttempts` is missing or negative, or `ohSoCloseOffer` lacks `unlock` or `ascension`.

#### Scenario: Missing token cap

- **GIVEN** an event entry without `maxStamina`
- **WHEN** the catalog loads
- **THEN** startup fails naming `lres`, the event unit and `maxStamina`
