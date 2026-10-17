## ADDED Requirements

### Requirement: HSE definitions serve their scoring under config.hse

A served `event-definitions` record of type `HomeScreenEvent` that has scoring data SHALL carry `config.hse` with this shape (every field required unless marked nullable); the server SHALL NOT send display strings, icons or loca keys:

```
config.hse: {
  variants: Variant[]                      // at least one
}
Variant: {
  variantKey: string                       // "default", a faction id, or a named run
  scope: "Personal" | "Global"             // Global: points are the community total
  tiers: Tier[]                            // at least one
  trackers: Tracker[]                      // may be empty
  modifiers: Modifier[]                    // may be empty
}
Tier: {
  tier: "High" | "Mid" | "Low" | "Default"
  milestones: { requiredPoints: int, rewardId: string, quantity: int }[]   // cumulative, strictly increasing
  repeat: { points: int, rewardId: string, quantity: int, cap: int | null } | null
}
Tracker: {
  kind: "KillUnits" | "DefeatWaves" | "DeployedUnitsOfFactionMajority" | "FullLineUpOfFaction"
      | "DonateUpgradeToGuildMember" | "DonateIntelToGuildProject" | "DispatchExpedition"
  points: int                              // per unit (kill, wave, battle, donation, dispatch)
  pointsByRarity: { [rarity: string]: int } | null
  gameModes: Restriction | null            // null: every mode
  enemy: UnitFilter | null
  player: UnitFilter | null
}
UnitFilter: {
  tags: Restriction | null                 // matched against a unit's traits or its alliance
  factionIds: Restriction | null
  unitIds: string[] | null
}
Restriction: { allowed: string[] | null, disallowed: string[] | null }
Modifier:
  | { kind: "UnitXp", multiplier: number | null, multiplierByRarity: { [rarity: string]: number } | null,
      sameFactionTeam: boolean, gameModes: Restriction | null, player: UnitFilter | null }
  | { kind: "StatBoost", healthPct: int, damagePct: int, armourPct: int,
      gameModes: Restriction | null, player: UnitFilter | null }
```

A `UnitXp` modifier carries exactly one of `multiplier` (flat) and `multiplierByRarity` (keyed by the unit's rarity, as in Training Rush and Warp Surge); `sameFactionTeam` is true when the bonus applies only to a full team of one faction. A `StatBoost` targets its units through `player.tags` (a trait) or `player.factionIds` (Faction Boost, 11th Edition). Fields of the other modifier kind are omitted.

Game-mode ids SHALL be the catalog names `Arena`, `TournamentArena`, `Onslaught`, `SalvageRun`, `Campaign`, `MirrorCampaign`, `EliteCampaign`, `MirrorEliteCampaign`, `CampaignEvent`, `Survival`, `LegendaryEvent`, `MachinesOfWarEvent`, `GuildWars`, `LinearHeroEvent`. A `Restriction` passes a value when the value is not in `disallowed` and, if `allowed` is set, is in `allowed`. Existing `config` keys (`gameModes`, `statBonus`, `xpBonusTiers`) SHALL be kept. A definition without scoring data (Against the Tide) SHALL have no `config.hse`.

#### Scenario: Psyker Boost is served with its ladder and tracker

- **WHEN** `event-definitions` is served
- **THEN** `hse-trait-boost-psyker` has `config.hse.variants[0]` with `variantKey` `default`, `scope` `Personal`, tiers `High`, `Mid`, `Low`
- **AND** the `High` tier's milestone `requiredPoints` are 20, 40, 60, 80, 105, 135, 165, 195, 230, 270, 320, 370, 420, 470, 520, 570, 645, 705, 765, its first milestone is `draft_abilityTokensUncommon` x 3, and `repeat` is 50 points of `draft_abilityTokensLegendary` x 1 with `cap` null
- **AND** the `Mid` tier ends at 605 with a repeat of 40, the `Low` tier ends at 345 with a repeat of 20
- **AND** it has one tracker `KillUnits`, `points` 1, `gameModes.allowed` [`Arena`, `SalvageRun`, `Onslaught`], `enemy.tags.disallowed` [`Summon`, `Steppable`], `player.tags.allowed` [`Psyker`]
- **AND** modifiers `StatBoost` 20/20/20 for `Arena` with `player.tags.allowed` [`Psyker`] and `UnitXp` multiplier 3 for `Arena`, `SalvageRun`, `Onslaught`

#### Scenario: Faction Boost has a variant per faction

- **THEN** `hse-faction-boost` has variants `DarkAngels` and `SpaceWolves` (High tier final milestone 1000, repeat 50) and `AdeptusMechanicus` (final milestone 2000, repeat 200), each with two `KillUnits` trackers: 2 points with `player.tags` allowing `Imperial` and disallowing `MachineOfWar`, and 1 point with `player.factionIds` allowing that faction

#### Scenario: Global events are marked

- **THEN** `hse-global-operation-imperator` has a single `default` variant with `scope` `Global` and a `Default` tier ending at 4,600,000

#### Scenario: Duplicate raid trackers are dropped

- **GIVEN** the Machine Hunt datamine has `killUnits` and `raidBattles` trackers with equal points and restrictions
- **THEN** only the `KillUnits` trackers are served

#### Scenario: No scoring data

- **THEN** `hse-against-the-tide` has no `config.hse`

### Requirement: An occurrence resolves to one variant

For an HSE occurrence the variant SHALL be: the variant whose `variantKey` equals `parameters.hseVariant` when that parameter is set; else the variant whose key equals `parameters.targetFactionId` when one exists; else the `default` variant. Clients SHALL apply the same rule; the server SHALL NOT add a resolved-variant field to calendar entries.

#### Scenario: Faction Boost for Dark Angels

- **GIVEN** the 2026-07-25 Faction Boost occurrence has `targetFactionId` `DarkAngels` and no `hseVariant`
- **THEN** it resolves to the `DarkAngels` variant

#### Scenario: Named run

- **GIVEN** the 2026-10-26 Faction Focus occurrence has `hseVariant` `october-2026`
- **THEN** it resolves to the `october-2026` variant (Default tier ending at 1220, repeat 50)

### Requirement: Scoring data is derived from the V1 datamine and validated

The raw `home-screen-event-scoring.json` SHALL be produced from the V1 datamine by the documented import: tier keys capitalized; non-endless rewards become cumulative milestones and the trailing endless reward becomes `repeat` (its value is the per-repeat step, `endlessCap` becomes `cap`); `rewardId:quantity` strings split (quantity 1 without a suffix); V1 game-mode ids mapped (`PvP` to `Arena`, `SyncPVP` to `TournamentArena`, `Waves` to `Onslaught`, `TreasureBeach` to `SalvageRun`, others unchanged); `raidBattles` trackers dropped; `unitXp` percentage modifiers divided by 100. Catalog loading SHALL fail when a scoring entry references an unknown or non-HSE definition, a variant has no tier, milestones are not strictly increasing, a repeat step or tracker points value is not positive, or an occurrence's `hseVariant` is not a variant of its definition.

#### Scenario: Non-increasing ladder fails the build

- **GIVEN** a tier whose milestones are 20, 40, 40
- **THEN** catalog loading fails naming the definition, variant and tier

#### Scenario: Unknown variant on an occurrence fails the build

- **GIVEN** an occurrence of `hse-defeat-waves` with `hseVariant` `run-3`
- **THEN** catalog loading fails naming the occurrence
