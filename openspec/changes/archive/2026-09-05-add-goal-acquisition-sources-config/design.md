## Context

See `proposal.md` — Why. Requirements are in `specs/goal-target-model/spec.md`. This is the
server companion to `tacticus-planner-apps`' `add-shop-shard-acquisition-sources`.

Current state:

- `GoalConfig` (`src/TacticusPlanner.Domain/Goals/GoalConfig.cs`) holds
  `AscensionFarmingConfig? AscensionFarming` (`Source` enum `Campaign|Onslaught|Both` +
  `ShardBattleIds` + `MythicShardBattleIds`) and `List<string>? FarmingLocationIds` — the
  latter shared by Unlock (shard nodes) and Rank/Ability (upgrade nodes).
- Persistence: `GoalConfiguration.cs` maps `config` as one jsonb column via
  `OwnsOne(entity => entity.Config).ToJson("config")`, with a nested
  `config.OwnsOne(c => c.AscensionFarming)` and `config.OwnsOne(c => c.Upgrade, u =>
  u.OwnsMany(x => x.Targets))`.
- Request/response: `CreateGoalConfigRequest` + `AscensionFarmingRequest` (strong
  `CampaignBattleId`s); `CreateCombinedGoalsRequest` reuses `CreateGoalConfigRequest` per
  goal; `UpdateGoalRequest` currently edits only `FarmingLocationIds` + `FarmingStrategy`;
  `GoalMapper` maps both directions; `GoalConfigResponse` / `AscensionFarmingResponse`.
- Validation: `CreateGoalValidator.IsValidConfig` (shape/enum, no catalog) +
  `GoalTargetValidationService` (catalog-aware; already builds the character's regular and
  mythic shard-battle-id sets).
- Migration precedent: `20260809171113_RedesignGoalsProjectsManagement` used raw
  `migrationBuilder.Sql(...)` and a `Down` that throws `NotSupportedException`.

## Goals / Non-Goals

**Goals**

- One persisted `acquisitionSources` list on `GoalConfig` that expresses any mix of
  Campaign / Onslaught / Shop sources and admits a future `Incursion` kind without a
  wire-contract break.
- Server validation of `kind`, per-kind `ids`, and entity/goal-type gating.
- A one-way data migration of every existing goal.
- Keep `FarmingLocationIds` working for Rank/Ability upgrade nodes.

**Non-Goals**

- No `Incursion` kind (that is `tacticus-planner-apps#106`).
- No server-side shop rotation / lock resolution — the API does not verify a shop *currently*
  offers a unit, only that the id is well-formed and names a real shop.
- No estimate/需求 math on the server; the client owns that.
- No dual-read transition window.

## Decisions

### DA1. `acquisitionSources` as `OwnsMany` of a small owned type

Domain: `sealed class AcquisitionSource { string Kind; List<string> Ids = []; }` and
`List<AcquisitionSource>? AcquisitionSources` on `GoalConfig`. EF:
`config.OwnsMany(c => c.AcquisitionSources)` inside the existing `config` jsonb mapping,
dropping `config.OwnsOne(c => c.AscensionFarming)`. Order is preserved (list, not set).

*Alternatives:* separate typed sub-objects per source kind (`CampaignSource`, `ShopSource`,
…) — rejected: every new kind is then a schema change, defeating the #106 forward-compat
goal. A flat `List<string>` of `"kind:id"` strings — rejected: loses the per-entry id list
grouping and needs ad-hoc parsing everywhere.

### DA2. `kind` is a validated string, not a CLR/OpenAPI enum

The wire type for `kind` is `string`; the allow-list lives in a server constant
(`AcquisitionSourceKinds = { "Campaign", "Onslaught", "Shop" }`). Adding `Incursion` later is
a one-line change with no OpenAPI enum-member churn and no client enum regen.

*Alternative:* a real enum — rejected: adding a member is a breaking OpenAPI schema change
for strict generated clients and forces lockstep regen for a purely additive concept.

### DA3. Shop offer id validated for shape + shop existence only

`GoalTargetValidationService` splits `<shopId>:<rewardType>` and checks that `shopId` is a
known shop and `rewardType` is `shards_<x>` / `mythicShards_<x>`. It does **not** resolve
shop rotation, day, power level, or lock conditions.

*Alternative:* port the client's shop resolver server-side and verify the offer is reachable
— rejected: duplicates non-trivial logic, couples goal validation to rotation data that
changes, and offers little safety (a stale offer id simply contributes nothing to the
client estimate).

### DA4. Coordinated replace, one-way migration

`AscensionFarming` and the Unlock shard role of `FarmingLocationIds` are removed outright;
the client change ships in the same release. No `acquisitionSources`-alongside-old-fields
phase. The migration `Down` throws `NotSupportedException` (data-lossy), matching the prior
goals migration. This supersedes the apps design's staged D7 transition — the apps
`design.md` / `tasks.md` should be trimmed to a single coordinated cutover.

*Alternative:* additive field + dual-read + later cleanup (three changes) — rejected: V2 is
pre-production with a single coordinated deploy; two code paths and three releases buy
nothing here.

### DA5. Validation split mirrors today

- `CreateGoalValidator.IsValidConfig`: `kind ∈ allow-list`; `Onslaught` entry has empty
  `ids`; `Shop` id matches `<shopId>:<rewardType>` syntactically. No catalog access.
- `GoalTargetValidationService`: `Campaign` ids ⊆ the character's shard-battle-id sets
  (regular ∪ mythic — the client re-splits); `Shop` `shopId` is a known shop; entity/
  goal-type gating (`Onslaught` ⇒ Character Ascension only; `Onslaught`/`Shop` rejected for
  MoW).

### DA6. Migration in SQL

`migrationBuilder.Sql(...)` using Postgres jsonb functions
(`jsonb_build_array` / `jsonb_build_object`, `#>`, key-drop `-`, `jsonb_set`), branching on
`goal_type` and the presence of `config->'ascensionFarming'` / `config->'farmingLocationIds'`
per the spec's migration scenarios. Schema-neutral (same `config` column). `Down` throws.

### DA7. Combined-create and V1 import

`CreateCombinedGoalsRequest` already nests `CreateGoalConfigRequest`, so it inherits the new
field with no separate DTO work. `V1GoalImportService`, which currently populates
`AscensionFarming` / `FarmingLocationIds` from V1 data, is rerouted to build
`AcquisitionSources` (V1 "campaign/onslaught/both" → the equivalent entries).

## Risks / Trade-offs

- **Migration correctness across the goal-type × old-field matrix** → the spec's migration
  scenarios become test cases; run the migration against a restored production-like snapshot
  and diff `acquisitionSources` before release.
- **Client/server skew if the two changes do not ship together** → single coordinated
  release; the client also guards `acquisitionSources == null` as unrestricted campaign.
- **`<shopId>:<rewardType>` cannot address two slots of one shop** offering the same unit →
  the id has no slot component; the two collapse to one addressable offer. Accepted; matches
  the apps-side trade-off, revisit only if shop data does this.
- **`kind` as a free string** means a typo passes shape validation and is caught only by the
  allow-list check → the allow-list check is mandatory in `IsValidConfig`; covered by the
  "unknown kind is rejected" scenario.

## Migration Plan

1. Domain + EF model change (DA1); generate the EF migration (model snapshot + the DA6 SQL
   data rewrite).
2. DTO / mapper / validator changes (DA5, DA7); regenerate OpenAPI.
3. Land together with `tacticus-planner-apps`' `add-shop-shard-acquisition-sources` (client
   types + goal-spec builder).
4. Verify: run the migration on a production-like snapshot; assert each spec migration
   scenario; smoke create/update/read/combined-create/V1-import.

Rollback: restore from backup (the migration is one-way by design).

## Open Questions

- Whether the API stores an explicit `[{ kind: "Campaign", ids: [] }]` when a client omits
  `acquisitionSources`, or stores `null` and treats `null` as unrestricted campaign at read
  time (as `FarmingLocationIds == null` already works). Leaning null + read-time
  interpretation; does not affect the wire contract, validation, or the migration.
