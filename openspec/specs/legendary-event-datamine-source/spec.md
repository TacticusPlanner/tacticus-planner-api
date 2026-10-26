# legendary-event-datamine-source Specification

## Purpose
Defines how the backend game catalog builds its Legendary Release Event datasets from the pinned datamine, which events it serves, and the few display and planning fields kept by hand because the game has no source for them.

## Requirements

### Requirement: Served events are picked by the curated file

The `lres` and `lre-battles` datasets SHALL serve one event per entry of `Data/curated/lre-curation.json`, keyed by the event unit id. Each entry SHALL name its datamine `liveEvents/legendaryHeroEvent.json` event, and loading SHALL fail, naming `lres` and the unit, when the event is missing or unlocks another unit.

#### Scenario: Four events

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** it serves `astarLysander`, `lostaBile`, `tauFarsight` and `votanUthar`, read from `legendary_hero_event_15`, `_16`, `_13` and `_14`

### Requirement: Only fields the game has no source for are curated

The curated entry SHALL hold the display `name`, `runStartDatesUtc`, and per track the display `name`, `enemies` label and link, `battlesPoints`, `allowedUnitsFilter` and `restrictionNames`. Everything else SHALL come from the datamine. Loading SHALL fail when a track's curated battle points or restriction names do not match its battle or restriction count.

#### Scenario: Restriction names line up with the game

- **GIVEN** a track whose battles carry three restrictions and whose curated entry lists two names
- **WHEN** the catalog loads
- **THEN** loading fails naming `lres`, the unit and the track

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
