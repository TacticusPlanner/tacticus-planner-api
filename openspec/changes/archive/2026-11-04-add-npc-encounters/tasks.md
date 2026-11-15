## 1. Models and registration

- [x] 1.1 Add `Models/NpcEncounters.cs` with `GameCatalogNpcEncounter` (`npcId`, `source`, `groupId`, `battles`), `GameCatalogNpcEncounterBattle` (`battleId`, `track`, `tier`, `number`, `challenge`, `role`, `rounds`) and the source / role string constants, serializing exactly the spec's field names and nullability. Verify with a serialization test that a battle with nulls and an empty `rounds` writes every field.
- [x] 1.2 Register `GameCatalogDatasets.NpcEncounters = "npc-encounters"` in `Served`, add the view list to `GameCatalogSnapshot`, and verify the project builds.

## 2. Source readers

- [x] 2.1 Add a Survival reader over `battles/battleSets.json` `survival_*` keys (wave `round` + `army` `"{npcId}:{N}"`). Verify a unit test pins `blackNpc3TraitorGuardsman` in `survival_crescendo_01` to rounds `[1, 3, 5, 12, 18, 22, 28, 34]`, and that a malformed entry throws naming `survival`.
- [x] 2.2 Add an Onslaught wave reader over `battles/onslaught.json` `tierWaves` (`tier`, `round`, `enemies`). Verify a unit test shows `tyranNpc1Hormagaunt` in tiers 1 to 21, and that a malformed entry throws naming `onslaught`.

## 3. Denormalization

- [x] 3.1 Implement `NpcEncounterDenormalizer.BuildNpcEncounters` for `campaign` / `campaignEvent` from `campaign-battles` + `campaign-definitions` (`releaseType` `event` splits them, event `track` = battle `type`). Verify tests cover a storyline battle and an Extremis challenge battle (`track`, `number`, `challenge`).
- [x] 3.2 Add `legendaryEvent` from `lre-battles` and `questEvent` from `quest-events`. Verify tests show `orksNpc1Grot` in `astarLysander-alpha-1` (`track` alpha, `number` 1) and `necroNpc1Warrior` in `astarCyrus` battle 1, and that quest board power-ups (`kind` object) are skipped.
- [x] 3.3 Add `incursion` from `incursions` (`tier` index, battle `id`, `challenge`). Verify with a test over a fixture tier.
- [x] 3.4 Add `guildRaid` from `raid-bosses`: boss / prime `questUnitId` gives a `boss` / `prime` entry in its own group, and encounter `fieldNpcIds` resolved through the merged unit-set `questUnitId` give `enemy` entries in the encounter's `unitSetId` group, with unit sets lacking `questUnitId` skipped and a `questUnitId` that is a served character (hero-based prime) skipped. Verify tests show `tyranNpc3Termagant` as `enemy` under `GuildBoss1Boss1TyranTervigonLeviathan` and no record for `astraPrimarisPsy`.
- [x] 3.5 Add `survival` and `onslaught` from the readers in group 2, and apply the indexing rules: `unit` / `machineOfWar` only, one record per `(npcId, source, groupId)`, de-duplicated battles, and the spec's record and battle order. Verify with tests for the order, the de-duplication, and the absence of any `_SyncPvp_` record.
- [x] 3.6 Wire the build into `GameCatalogLoader.Load()` after every source view exists, with a `HashServed` entry. Verify a loader test shows two loads serving identical `npc-encounters` hashes.

## 4. Validation

- [x] 4.1 Add `Validation/NpcEncounterValidation.cs` (`InvalidNpcEncounter`): an unknown enemy npc id, a guild raid `questUnitId` that is neither an npc nor a character, or an empty dataset fails the load, naming `npc-encounters`, the source and the id. Verify one failing-fixture test per rule.

## 5. Endpoint, manifest and contract

- [x] 5.1 Add the anonymous `GET /api/v1/game-catalog/npc-encounters` endpoint in `GetGameCatalogDatasetEndpoints.cs`. Verify an API test fetches it in the dataset envelope.
- [x] 5.2 Update `GameCatalogSnapshotTests.GameCatalogManifestMatchesSnapshot.verified.txt` for the new dataset hash and moved `schemaHash`. Verify `dotnet test tests/TacticusPlanner.Api.Tests` passes.
- [x] 5.3 Build to regenerate `artifacts/openapi/TacticusPlanner.Api.json`. Verify the diff adds only the `npc-encounters` path and its schema, and that the apps companion `add-npc-encounters` schema matches it.
- [x] 5.4 Measure the served `npc-encounters` payload size (uncompressed and gzip) from a local run and record it in the PR description. If it exceeds 500 KB gzip, stop and raise it before continuing (design risk "Payload size").

## 6. Docs

- [x] 6.1 Add an `npc-encounters` entry to `.agents/skills/game-catalog-data/SKILL.md` (sources, derivation from served views, the `questUnitId` resolution, no timing). Verify it names no stale file path.

## 7. Gates

- [x] 7.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` and `dotnet test TacticusPlanner.slnx -c Release --no-build`, and verify all three pass.

## 9. Campaign enemy levels

- [x] 9.1 Read every campaign enemy index as 1-based in `CampaignEnemyStatRow` and update the skill doc and the `campaign-battles-datamine-source` spec. Verify with `CampaignDatamineReaderTests`, known battles pinned against the game's power, and the power formula re-run over every campaign battle.
