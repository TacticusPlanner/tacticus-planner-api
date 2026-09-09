## 1. Served projection

- [ ] 1.1 Add `string? QuestUnitId` as the last optional field on
  `GameCatalogRaidBossView` (`Models/RaidBosses.cs`) with
  `[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`,
  matching the other optional fields; verify `dotnet build` succeeds.
- [ ] 1.2 Map `rawUnitSet.QuestUnitId` onto the view in `RaidBossDenormalizer`;
  verify by loading the catalog in a test and asserting a known unit set
  (`GuildBoss1Boss1TyranTervigonLeviathan` → `tyranNpc3Termagant`) exposes it.

## 2. Tests

- [ ] 2.1 Extend the `GameCatalog.Tests` raid-boss denormalization test: assert
  `questUnitId` round-trips for a unit set that defines one and is absent from
  the serialized payload for one that does not; verify `dotnet test` passes.
- [ ] 2.2 Regenerate the catalog manifest snapshot
  (`GameCatalogSnapshotTests.GameCatalogManifestMatchesSnapshot.verified.txt`)
  — the served `raid-bosses` hash changes; verify the snapshot test passes and
  the diff touches only the `raid-bosses` entry.

## 3. Gates

- [ ] 3.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`,
  `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and
  `dotnet test TacticusPlanner.slnx -c Release --no-build`; all green.
- [ ] 3.2 Confirm the regenerated `artifacts/openapi` diff is empty (no endpoint
  contract change — the served dataset schema is not part of the OpenAPI
  artifact) and note the companion apps change `add-raid-boss-portraits`
  consumes the new field.
