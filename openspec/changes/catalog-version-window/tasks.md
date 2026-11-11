# Tasks

## 1. Two release folders and pins

- [ ] 1.1 Move `Data/datamine/{raw,REF,files.sha256}` to `Data/datamine/latest/` (1.44.87.3088). Update the csproj `EmbeddedResource` items to embed `Data\datamine\<release>\raw\**` as `datamine/<release>/<path>` and each `<release>/raw/VERSION`. Verify by building and listing the assembly's manifest resource names in a test: none is still under a bare `datamine/` prefix.
- [ ] 1.2 `scripts/pull-datamine.cs`:
  - Add the roll mode (the default with a tag). It validates every listed path at the target, then moves `latest` to `previous`, then fills `latest`.
  - Add `--refresh <latest|previous> <tag-or-sha|--source dir>`.
  - Fill `previous/` with `--refresh previous v1.43.157.2989`.
  - Verify by running both modes against a scratch copy: roll leaves the old `latest` byte-identical in `previous`; a missing upstream path changes neither folder.
- [ ] 1.3 `DatamineSourceTests` runs per release:
  - REF format and no `local` pin;
  - checksums and the allowlist;
  - a new check that `latest/raw/VERSION` is newer than `previous/raw/VERSION`.
  - Verify with `dotnet test tests/TacticusPlanner.GameCatalog.Tests --filter DatamineSourceTests`, including a deliberately swapped-versions case.

## 2. Per-release loading

- [ ] 2.1 Make `DatamineFiles` an instance bound to a release, opening `datamine/<release>/<raw path>`. Keep the leaf-name guard. Verify with the existing `DatamineFiles` tests rewritten per release, plus one test that `previous` never reads `latest`'s copy.
- [ ] 2.2 Replace the static `GameCatalogRelease` with per-snapshot release metadata: `GameVersion` from the release's `raw/VERSION`; `Version` hashed over that release's datamine resources plus the curated resources. Verify with a test that the two releases have different `GameVersion`/`Version`, and that a curated-only change moves both `Version`s.
- [ ] 2.3 `GameCatalogLoader.Load(release, asOf)`. Verify both snapshots build and pass `GameCatalogValidator` in a new `CatalogWindowTests` (no pinned counts).
- [ ] 2.4 `IGameCatalogProvider`:
  - Add `Latest`, `Previous`, `Window` and `Get(gameVersion)`. Keep `Current` as an alias of `Latest`.
  - Build both eagerly in `EmbeddedGameCatalogProvider`, and rebuild both on the daily `asOf` roll.
  - Make the startup check in `Program.cs` resolve both, naming the failing release.
  - Verify with a provider test, and that a broken `previous` fixture fails startup with the release named.
- [ ] 2.5 Keep the game-fact tests (`QuestEventsDenormalizerTests`, `UnitDatamineReaderTests`, …) on `Latest` explicitly. Verify `dotnet test tests/TacticusPlanner.GameCatalog.Tests` passes.

## 3. Serving the window

- [ ] 3.1 `GET /api/v1/game-catalog/versions`: anonymous, latest first, with an ETag and `If-None-Match`. Verify with an Api.Tests integration test covering the shape, the order and the 304.
- [ ] 3.2 `GET game-catalog/manifest?gameVersion=`:
  - no parameter gives `Latest`, unchanged;
  - a served version gives that release;
  - anything else gives 404 with issue code `notFound`.
  - ETag and `Vary` are as today, with the ETag per release.
  - Verify with integration tests for all three cases.
- [ ] 3.3 `ServedDatasetEndpoint<T>`:
  - Resolve `{envelopeHash}` across both snapshots' cached envelopes; return 404 when neither has it.
  - The bare route serves `Latest`.
  - Verify with a test that fetches a `previous` manifest URL and gets `GameVersion` `1.43.157.2989`, and that a `factions` entry has the same `hash` but different URLs in the two manifests.
- [ ] 3.4 Update the manifest snapshot test to snapshot both manifests (`…Latest.verified.txt`, `…Previous.verified.txt`). Verify `GameCatalogSnapshotTests` passes.

## 4. User setting

- [ ] 4.1 `UserSettingsData.CatalogVersion` (string, null for Latest). No EF Core migration: it lives in the existing `settings` JSON column. Verify with a persistence test that a row written before the field reads `null`, and with `dotnet ef migrations has-pending-model-changes` reporting none.
- [ ] 4.2 `GET settings` returns `catalogVersion` (`Latest` or the stored version) and `effectiveCatalogVersion`, resolved against the window at read time without writing. Verify with integration tests: a new profile, a previous-release pin, and a pin outside the window (simulated with a test provider).
- [ ] 4.3 `PUT settings` takes `catalogVersion?`:
  - absent keeps the stored value;
  - `latest` (case-insensitive) clears it;
  - a window version stores it;
  - anything else is a 400 `validation` error naming `catalogVersion`, with nothing stored.
  - The revision check is unchanged.
  - Verify with integration tests for each case, including an older-client body without the field.
- [ ] 4.4 Add an `IEffectiveCatalog` scoped service that resolves the authenticated profile's snapshot, cached per request. Verify with a unit test of the resolution rules.

## 5. Consumers

- [ ] 5.1 Add a union id lookup over the window (built once per snapshot pair). Switch `GoalTargetValidationService`/`GameCatalogGoalLookups`, `LegendaryEventCatalogValidator`, `LegendaryEventPlanEndpointBase`, `ProjectTargets`, and the battle, campaign-event and incursion progress endpoints to it. Verify with tests: a goal for `lostaFearmonger` and a quest-event battle override for `astarCyrus` are both accepted; an id in neither release is rejected as today.
- [ ] 5.2 Switch `LegendaryEventPlanProjection`, `LegendaryEventPlanWriter`, `LegendaryEventRunRecordRecorder` and `RosterSnapshotFactory` to `IEffectiveCatalog`, so they compute on and stamp the effective release's `Version`. Ids missing from that release are skipped, not thrown on. Verify with an integration test: a plan saved by a previous-release profile records the previous `Version`.
- [ ] 5.3 Pin `PlayerDataTransformer` and `GuildRaidStatusService`/`Projector` to `Latest` explicitly (not through `Current`). Have V1 imports validate with the union lookup and keep stamping `v1-import`. Verify with the existing sync, raid-status and import tests.
- [ ] 5.4 Find any remaining `.Current` reads with `grep -rn "\.Current\b" src | grep -i catalog`. Each one is either moved to an explicit `Latest`/union/effective call, or listed in design.md with its reason. Then mark `Current` `[Obsolete]` to keep new uses out. Verify the build has no new warnings beyond the intended obsoletion sites, which should be none.

## 6. Contract, docs and gates

- [ ] 6.1 Regenerate `artifacts/openapi`. Verify it contains `GameCatalogVersions`, the `gameVersion` query parameter, and `catalogVersion`/`effectiveCatalogVersion` on the settings schemas. Hand the diff to the companion apps change.
- [ ] 6.2 Update `AGENTS.md` (the datamine and game-catalog sections) and the `game-catalog-data` skill for the two release folders, roll and refresh. Verify the documented commands run as written.
- [ ] 6.3 Measure startup time and working set with one snapshot versus two (Aspire, `api` resource). Record both in the PR body. Verify the numbers are present.
- [ ] 6.4 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` and `dotnet test TacticusPlanner.slnx -c Release --no-build`. Verify all three pass.

## Workflow follow-up

- Update the workspace `tp-bump-datamine` skill: a bump rolls the window in both repos (`pull-datamine -- <tag>` here, and the apps two-pin pull), and a forced re-run uses `--refresh latest`.
- Amend ADR 0004 in `tacticus-planner-docs` with the two-release window, the per-user selection and the fallback rule.
- Apply the companion `tacticus-planner-apps` change after this one; archive both only when both PRs are green with review threads resolved.
