## 0. Coordination

- [ ] 0.1 Companion `tacticus-planner-apps` change `add-project-deadline-targets`; this API half merges first

## 1. Domain and persistence

- [ ] 1.1 `ProjectTargetKind` gains `BattlePass` (`battlePass`), `GuildRaidSeason` (`guildRaidSeason`), `GuildWarSeason` (`guildWarSeason`), `CustomDate` (`customDate`)
- [ ] 1.2 Nullable `TargetRun` (int), `TargetSeason` (int), `TargetDate` (`DateOnly`) on `Project`; `ProjectConfiguration`
- [ ] 1.3 `dotnet ef migrations add AddProjectDeadlineTargets` (three nullable columns, no backfill); review the generated migration and model snapshot

## 2. Contract and validation

- [ ] 2.1 `ProjectTargetDto(Kind, EventId?, Run?, Season?, Date?)`; `ProjectTargets.Validate` applies the per-kind rules and keeps the stored-target exemption (whole-target equality); `Apply`/`ToDto` cover every kind
- [ ] 2.2 Create/update validators name the offending field (`target.run`, `target.season`, `target.date`, `target.eventId`, `target`)
- [ ] 2.3 Endpoint tests for every scenario in `specs/project-event-target`
- [ ] 2.4 Regenerate and review `artifacts/openapi`; note the new `ProjectTargetDto` fields and `ProjectTargetKind` values for the apps change

## 3. Staging transfer

- [ ] 3.1 `StagingTransferBundle` carries `TargetRun`, `TargetSeason`, `TargetDate`; exporter and importer map them; round-trip test with a Legendary Event target with `TargetRun` 1, a season target and a date target, checking all three values survive export and import

## 4. Verification

- [ ] 4.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
- [ ] 4.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore`
- [ ] 4.3 `dotnet test TacticusPlanner.slnx -c Release --no-build`
