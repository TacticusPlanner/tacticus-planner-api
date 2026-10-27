# legendary-event-datamine-source Specification

## Purpose
Defines how the backend game catalog builds its Legendary Release Event datasets from the pinned datamine, which events it serves, and the run starts kept by hand because the game has no source for them.

## Requirements

### Requirement: Served events are picked by the curated file

The `lres` and `lre-battles` datasets SHALL serve one event per entry of `Data/curated/lre-curation.json`, keyed by the event unit id. The event served SHALL be the one `liveEvents/legendaryHeroEvent.json` event whose `unlockableHero` is that unit, and its `name` the unit's name from the datamine lineup. Loading SHALL fail, naming `lres` and the unit, when no event or more than one unlocks the unit.

#### Scenario: Four events

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** it serves `astarLysander`, `lostaBile`, `tauFarsight` and `votanUthar`, read from `legendary_hero_event_15`, `_16`, `_13` and `_14`

### Requirement: Only run starts are curated

The curated entry SHALL hold only `runStartDatesUtc`, which the game config does not carry. Everything else SHALL come from the datamine.

#### Scenario: Curated entry

- **WHEN** the catalog loads `lre-curation.json`
- **THEN** each entry holds nothing but `runStartDatesUtc`

### Requirement: Battle points are one per spawned enemy

A battle's `battlesPoints` value SHALL be the number of enemies across its waves (the length of every wave's `army` summed).

#### Scenario: Lysander's first battle

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** `astarLysander` alpha battle 1, with 32 enemies across its waves, has 32 battle points

### Requirement: Track names and filters follow the disallowed factions

Every battle of a track SHALL disallow the same factions, and they SHALL include exactly one alliance's whole roster (alliances from the datamine lineup). The track SHALL serve `allowedUnitsFilter` as that alliance excluded, then each extra faction excluded in game order, and V1's `name`: the lane label then `(No {alliance} or {faction}…)`. Loading SHALL fail when no single alliance is barred.

#### Scenario: Extra faction

- **GIVEN** a gamma track that disallows the Chaos roster and `Orks`
- **WHEN** the catalog loads
- **THEN** it is named `Gamma (No Chaos or Orks)` with filters `Alliance Chaos` and `Faction Orks`, both excluding

### Requirement: Display names use V1's labels

The `enemies` label SHALL be the name of the lane's `factionId` and its link `https://tacticus.wiki.gg/wiki/{label with spaces as _}#NPC_Units`. Restriction names SHALL follow V1's `objectiveDisplayName` (for example `No Bolter`, `Min 5 hits`, `Terminator`). Faction names SHALL be V1's (`Adeptus Mechanicus`, `T'au Empire`) and trait names V1's labels. A faction or trait with no label SHALL fail the load.

#### Scenario: Restriction names

- **GIVEN** objectives `MinHits 5`, `NotTrait TerminatorArmour` and `HasNoRangedAttack`
- **WHEN** the catalog loads
- **THEN** they are named `Min 5 hits`, `No Terminator` and `Melee`

### Requirement: Lane points come from the Acing objective

Each battle's first objective SHALL be `Acing`; its score is that battle's `defeatAll` value and the first battle's is the track's `killPoints`. A battle without a leading `Acing` objective SHALL fail the load.

#### Scenario: Defeat-all points

- **GIVEN** a track whose 18 battles have `Acing` scores of 37, except 45 in battle 18
- **WHEN** the catalog loads
- **THEN** the track has `killPoints` 37 and `defeatAll` seventeen 37s followed by 45

### Requirement: Restrictions come from the remaining battle objectives

The objectives after `Acing` SHALL be the same in every battle of the track and are served, in order, as `unitsRestrictions` with their `score` as `points`. `NotDamageType` and `NotTrait` SHALL serve as excluding `DamageType` and `Trait` filters, and `HasRangedAttack` / `HasNoRangedAttack` as an including / excluding `AttackType` `Ranged` filter. An unknown objective type SHALL fail the load.

#### Scenario: Excluding filter

- **GIVEN** a track objective `{ objectiveType: "NotDamageType", objectiveTarget: "Bolter", score: 30 }`
- **WHEN** the catalog loads
- **THEN** the restriction is served with `points` 30 and filter `{ kind: "DamageType", target: "Bolter", exclude: true }`

### Requirement: Missions are generated from the game's quest tasks

The regular and premium missions SHALL be the event's `standard` and `premium` quest groups, one line per quest, its tasks' texts joined by ", " in V1's wording (for example `Defeat 5 waves of enemies in any wave-based game mode, Deal 1000 damage`). Faction and trait labels SHALL be the reverse of the mission parser's alias tables. A task kind or label with no text SHALL fail the load.

#### Scenario: Uthar's premium missions follow the game

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** `votanUthar` premium mission 4 is `Win 5 battles without deploying any Resilient characters` (V1's hand-kept file had Farsight's `Big Target` mission)

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
