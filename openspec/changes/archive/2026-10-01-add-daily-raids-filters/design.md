## Context

V1 `CampaignsService.getEnemiesAndAllies(campaign)` (`tacticusplanner/src/fsd/4-entities/campaign/campaigns.service.ts`) is the source for allies. It maps each campaign (and its Elite/Mirror/Challenge variants) to `{ alliance, factions }`. V2 groups campaigns differently: one raw file per `{campaign, type}` track for the storyline (`campaign1-4`, `elite1-4`, `mirror1-4`, `eliteMirror1-4`) and one per event (`eventCampaign1-6`, `Standard` + `Extremis` battles together). Each raw file already carries group metadata (`groupId`, `faction`, `releaseType`, `coreCharacters`, `types`) and is loaded into `GameCatalogCampaignGroup`; `BuildBattleView` already copies group-level data (`campaignGroupId`) onto each served battle.

## Goals / Non-Goals

**Goals:** serve allied alliance/factions on every campaign battle so the client matches V1 filter semantics without a hardcoded table; fail catalog load if any group lacks allies.

**Non-Goals:** per-node allies (V1 has none); enemy-side changes; display text (ids only); a `SchemaVersion` bump; a new dataset.

## Decisions

1. **Raw authoring at group level.** Add `alliesAlliance` and `alliesFactions` next to `faction`/`releaseType` in each `campaign-battles-*.json`. Mapping (V2 group id from V1 campaign), source V1 `getEnemiesAndAllies`:

   | Group ids | V1 campaign | alliesAlliance | alliesFactions |
   |---|---|---|---|
   | `campaign1`, `elite1` | Indomitus (+Elite) | Imperial | all Imperial factions |
   | `mirror1`, `eliteMirror1` | Indomitus Mirror (+Elite) | Xenos | Necrons |
   | `campaign2`, `elite2` | Fall of Cadia (+Elite) | Chaos | all Chaos factions |
   | `mirror2`, `eliteMirror2` | Fall of Cadia Mirror (+Elite) | Imperial | all Imperial factions |
   | `campaign3`, `elite3` | Octarius (+Elite) | Xenos | Orks |
   | `mirror3`, `eliteMirror3` | Octarius Mirror (+Elite) | Imperial | all Imperial factions |
   | `campaign4`, `elite4` | Saim-Hann (+Elite) | Xenos | Aeldari |
   | `mirror4`, `eliteMirror4` | Saim-Hann Mirror (+Elite) | Chaos | all Chaos factions |
   | `eventCampaign1` | Adeptus Mechanicus event | Chaos | DeathGuard, WorldEaters |
   | `eventCampaign2` | Tyranids event | Imperial | Ultramarines, BloodAngels |
   | `eventCampaign3` | T'au event | Xenos | Genestealers, Tyranids |
   | `eventCampaign4` | Death Guard event | Imperial | Sisterhood, BlackTemplars |
   | `eventCampaign5` | Adepta Sororitas event | Chaos | WorldEaters, BlackLegion |
   | `eventCampaign6` | Dark Angels event | Xenos | Necrons |

   "All Imperial/Chaos factions" is written out explicitly as faction ids (from V1 `factionData` filtered by alliance, reconciled with the catalog's `Data/units/units-*.json` alliance field), not computed at runtime, so the served value is reviewable and a new faction does not silently change old campaigns. The event-id to V1-campaign pairing was checked against each group's `faction` and file name (`eventCampaign1` is `death-guard-vs-admech`, which is V1 AMS, and so on); re-verify at apply time.
2. **Served shape.** `GameCatalogCampaignBattleView` gains `AlliesAlliance: string` and `AlliesFactions: IReadOnlyList<string>` (serialized `alliesAlliance`, `alliesFactions`). `BuildBattleView` receives the group and copies both. The raw per-battle record `GameCatalogCampaignBattle` is unchanged.
3. **Validation.** In `RequiredFieldsValidation`, require `alliesAlliance` and a non-empty `alliesFactions` per group; in `ReferenceValidation`, require `alliesFactions` ids to exist in the faction set and `alliesAlliance` to be a known alliance value. Load fails fast, per the `game-catalog-data` pipeline.
4. **No schema bump.** The change is additive; per the `game-catalog-data` skill `SchemaVersion` moves only on a breaking shape change. The `campaign-battles` dataset hash and `sourceHash` move; the `campaign-definitions` hash does not. Promote the manifest Verify snapshot after reviewing that only those hashes changed. `GameVersion` is not bumped.
5. **No EF Core migration, no new endpoint.** The existing `campaign-battles` endpoint serves the new fields; review `artifacts/openapi` if the battle view is in it.

## Risks / Trade-offs

- [Event pairing mistake] Mapping an event id to the wrong V1 campaign gives the wrong allies. Mitigation: the table above is checked against `faction` metadata and a catalog test asserts the exact value per group.
- [Hand-authored explicit faction lists drift when a faction is added] Accepted; a new faction is added to the lists deliberately in the same change that adds it.

## Open Questions

None blocking. Whether any V2-only campaign group has no V1 equivalent is checked at apply time (none today).

## Companion change

`tacticus-planner-apps` change `add-daily-raids-filters` (same name). **Apply and release this API change first**, because the apps schema requires `alliesAlliance`/`alliesFactions`. The apps change is applied second.
