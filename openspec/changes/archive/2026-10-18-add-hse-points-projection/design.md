# Design

## Context

`event-definitions.json` has 21 `HomeScreenEvent` definitions; only Faction Boost, Faction Focus and Warp Surge carry a `config` (game modes, stat bonus, XP tiers). `Config` is served as an opaque `JsonElement`. The client (`packages/game-catalog`) parses `config` as `unknown`. Companion: apps change `add-hse-points-projection`; contract: `event-definitions[].config.hse`.

## Decisions

### D1. A separate raw dataset, merged at denormalization

Scoring lives in its own raw file `Data/events/home-screen-event-scoring.json` (an array keyed by `definitionId`) instead of being hand-edited into `event-definitions.json`. It is large (about 20 events x 3 tiers x 20 milestones), changes on every datamine refresh and is generated, while definitions are authored. `EventsDenormalizer` adds it under `config.hse` of the matching served definition. One served structure; the raw file is never served.

### D2. Variants instead of one scoring per definition

The same definition can have different ladders: Faction Boost ends at 1000 points for Dark Angels and Space Wolves but 2000 for Adeptus Mechanicus; Faction Focus has a generic and an October 2026 ladder; Defeat Waves has two runs; the 11th-edition weeks have personal and global (Blood Angels / Orks) ladders. Each definition therefore has a list of variants with a `variantKey`. Selection for an occurrence: `parameters.hseVariant` if set, else `parameters.targetFactionId` if a variant has that key, else `default`. The client applies the same rule (it is stated in the spec).

### D3. Mapping from the V1 datamine

| V1 file | definition | variantKey |
|---|---|---|
| trait-boost-psyker | hse-trait-boost-psyker | default |
| trait-boost-flying | hse-trait-boost-flying | default |
| hse-trait-boost-rapid-assault | hse-trait-boost-rapid-assault | default |
| hse-trait-boost-terminator-armour | hse-terminator-boost | default |
| for-the-emperor / for-the-dark-gods | hse-for-the-emperor / hse-for-the-dark-gods | default |
| faction-boost-dark-angels / -space-wolves / -adeptus-mechanicus | hse-faction-boost | DarkAngels / SpaceWolves / AdeptusMechanicus |
| faction-focus, hse-faction-focus-october-2026 | hse-faction-focus | default, october-2026 |
| defeat-waves, defeat-waves-2 | hse-defeat-waves | default, run-2 |
| hse-11th-edition-weekN, global-...-weekN-blood-angels / -orks | hse-11th-edition-week-N | default, global-blood-angels, global-orks |
| global-hse-operation-imperator | hse-global-operation-imperator | default |
| conflict-operations | hse-global-conflict-operations | default |
| arsenal-of-war | hse-arsenal-of-war | default |
| squig-smash | hse-squig-smash | default |
| machine-hunt, warp-surge, rarity-training-rush, kill-tyranids | hse-machine-hunt, hse-warp-surge, hse-training-rush, hse-purge-order | default |

`hse-against-the-tide` has no datamine and gets no `config.hse`.

The import is a one-off script kept under `scripts/` (or the skill), not a build step; the committed raw file is the source of truth.

### D4. Normalization at import

- Tier keys `high`/`mid`/`low`/`default` become `High`/`Mid`/`Low`/`Default`.
- `tieredProgressRewards`: entries without `endless` become `milestones` (cumulative `requiredPoints`), the trailing `endless` entry becomes `repeat` (its `requiredProgress` is already the per-repeat step).
- `chestRewardId` `draft_abilityTokensRare:4` becomes `{ rewardId: "draft_abilityTokensRare", quantity: 4 }`; no suffix means quantity 1.
- Game-mode ids: `PvP` to `Arena`, `SyncPVP` to `TournamentArena`, `Waves` to `Onslaught`, `TreasureBeach` to `SalvageRun`; all other ids (`Campaign`, `MirrorCampaign`, `EliteCampaign`, `MirrorEliteCampaign`, `CampaignEvent`, `Survival`, `LegendaryEvent`, `MachinesOfWarEvent`, `GuildWars`, `LinearHeroEvent`) unchanged.
- Trackers of type `raidBattles` (campaign sweep) are dropped: in every datamine each one's modes are covered by a `killUnits` tracker with equal points (their restrictions can be written differently, as in Machine Hunt's allow list against its kill tracker's disallow list).
- Tracker `traitRestrictions` (enemy traits or alliance), `factionRestrictions` (enemy faction), `unitId` (enemy unit), `playerTraitRestrictions` (own unit traits or alliance) and `playerFactionRestrictions` (own unit faction) become the `enemy` and `player` filters.
- Modifiers `unitXp` (`modifier` 300 with `percentageModifier`) become `UnitXp` with `multiplier` 3; `battleEnvironmentAbility` `EnvStatBoost:*` becomes `StatBoost` with the ability constants `healthPct`, `dmgPct`, `armorPct` and its `trait` (or `factionId`) as the player filter; other environment abilities (such as Squig Smash's) are skipped. Rarity-scaled XP (`modifierByRarity`) becomes `multiplierByRarity`, and the same-faction-team flag becomes `sameFactionTeam`.
- Display strings (`locaKey`, descriptions) and offers are not imported.

### D5. Validation

The loader fails when: a scoring entry names an unknown definition or a non-HSE definition; a variant has no tier; milestones are not strictly increasing; a repeat step is not positive; a tracker's `points` is not positive; an occurrence's `hseVariant` names a variant the definition does not have; a definition has scoring but no `default` variant and its occurrences can resolve to none.

## Risks

- The datamine is a snapshot. A new event run with different numbers needs a refresh; the skill step covers it.
