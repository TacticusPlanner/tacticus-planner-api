## 1. Served model

- [x] 1.1 Add `FactionId`, `Alliance`, and `Kind` (string: `unit` / `machineOfWar` / `object`) to `GameCatalogNpc` in `src/TacticusPlanner.GameCatalog/Models/Npcs.cs`, with an XML doc comment on `Kind` stating the derivation rule, and verify `dotnet build` succeeds with the raw deserialization unchanged (the raw per-faction file still binds to `GameCatalogFactionNpcs`).

## 2. Denormalization and validation

- [x] 2.1 Update `GameCatalogDenormalizer.BuildNpcs` in `Denormalization/NpcDenormalizer.cs` to stamp each NPC with its owning file's `FactionId` / `Alliance` and to derive `Kind` (`object` when the source key is `npcs-objects`, else `machineOfWar` when `Traits` contains `MachineOfWar`, else `unit`), preserving the existing ordinal-by-key then source order; verify with new `NpcDenormalizerTests` covering: faction/alliance stamping, `object` from the objects file, `machineOfWar` by trait for a `MoW` and a `Mow` spelled id, `unit` default, a zero-stat unit still `unit`, order preservation, and that the served record's property set contains no `icon`.
- [x] 2.2 Add an NPC validation partial (`Validation/NpcValidation.cs`) that fails the load when a record from `npcs-objects` carries the `MachineOfWar` trait, and verify with a `GameCatalogValidator` test that the conflicting fixture throws and the real embedded data loads cleanly (`GameCatalogLoaderTests`).

## 3. Contract artifacts

- [x] 3.1 Update `tests/TacticusPlanner.Api.Tests/GameCatalogSnapshotTests.GameCatalogManifestMatchesSnapshot.verified.txt` with the new `npcs` hash (only that entry changes; `schemaVersion` stays `4`), and verify `GameCatalogSnapshotTests` passes.
- [x] 3.2 Build and verify the regenerated `artifacts/openapi/TacticusPlanner.Api.json` lists `factionId`, `alliance`, and `kind` on `TacticusPlannerGameCatalogModelsGameCatalogNpc`, then commit the regenerated artifact.
- [x] 3.3 Run the API through Aspire (`aspire run --project orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj`), wait for `api` healthy, and verify `GET /api/v1/game-catalog/npcs` returns `necroBossWarden` with `factionId` `Necrons` / `alliance` `Xenos` / `kind` `unit`, `deathNpcMoWCrawler` with `kind` `machineOfWar`, and `LootObj_AmmoBox` with `factionId` `Objects` / `kind` `object`.

## 4. Companion coordination and gates

- [x] 4.1 Confirm the companion `tacticus-planner-apps` change `add-npc-library-page` references the same three field names and the `kind` value set, and note in this change's design if either side renamed anything.
- [x] 4.2 Run the repository gates and verify all pass: `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`.
