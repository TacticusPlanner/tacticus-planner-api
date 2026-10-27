## MODIFIED Requirements

### Requirement: Scoring data is derived from the V1 datamine and validated

The scoring SHALL be read at load from the pinned datamine copies `gameconfig/liveEvents/homeScreenEvent.json` (an event's power tiers are the entries named `<event>_tier_high`, `_mid`, `_low`; an entry without that suffix is a `Default` tier), `gameconfig/loot/tieredProgressRewards.json` (each tier's ladder, by `rewardProgressId`) and `gameconfig/units/abilities.json` (`EnvStatBoost:` constants). A fixed table maps each game event to a definition id, variant key and scope; game events it does not list are ignored, and a listed event missing from the datamine SHALL fail startup. A tier whose ladder the game no longer has SHALL be skipped. The mapping: tier keys capitalized; non-endless rewards become cumulative milestones and the trailing endless reward becomes `repeat` (its value is the per-repeat step, `endlessCap` becomes `cap`); `rewardId:quantity` strings split (quantity 1 without a suffix); the game's mode ids mapped (`PvP` to `Arena`, `SyncPVP` to `TournamentArena`, `Waves` to `Onslaught`, `TreasureBeach` to `SalvageRun`, others unchanged); `raidBattles` (campaign sweep) trackers dropped, since each one's modes are covered by a `killUnits` tracker with equal points; `unitXp` percentage modifiers divided by 100. Loading SHALL fail, naming the event, on shapes it cannot represent: an empty `allowed` list, trackers or modifiers that differ between tiers, an unknown tracker or modifier type, and a `unitXp` operator other than `multiply`. Catalog loading SHALL fail when a scoring entry references an unknown or non-HSE definition, the definition's authored `config` is not an object or already has `hse`, a variant has no tier or repeats a tier, milestones are not strictly increasing, a repeat step or tracker points value is not positive, a game mode is not a catalog name, a modifier kind is unknown, a `UnitXp` modifier lacks exactly one of `multiplier` and `multiplierByRarity`, or an occurrence's `hseVariant` is not a variant of its definition.

#### Scenario: Non-increasing ladder fails the build

- **GIVEN** a tier whose milestones are 20, 40, 40
- **THEN** catalog loading fails naming the definition, variant and tier

#### Scenario: Unknown variant on an occurrence fails the build

- **GIVEN** an occurrence of `hse-defeat-waves` with `hseVariant` `run-3`
- **THEN** catalog loading fails naming the occurrence

#### Scenario: Defeat Waves serves the game's current ladders

- **WHEN** `event-definitions` is served from datamine `v1.43.157.2989`
- **THEN** `hse-defeat-waves`'s `default` variant has the game's current `High` and `Mid` ladders and no `Low` tier (the game has no `hse_defeat_waves_tier_low` ladder), and its `run-2` variant keeps `High`, `Mid` and `Low`
