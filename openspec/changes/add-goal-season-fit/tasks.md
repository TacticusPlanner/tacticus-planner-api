# Tasks

## 1. Catalog data

- [x] 1.1 Make `guild-raid-season` a `Fixed` recurrence (14 / 13 days, anchor 2026-10-21T05:00:00Z) in `Data/curated/event-definitions.json`.
- [x] 1.2 Add `GuildRaidSeasonProjectsAsAFourteenDayCycle` to `EventsDenormalizerTests` pinning the recurrence and its projection.
- [ ] 1.3 Update the `event-definitions` hash in `GameCatalogSnapshotTests.GameCatalogManifestMatchesSnapshot.verified.txt` (from the CI/local Verify diff; no .NET SDK in the authoring session).

## 2. Spec

- [x] 2.1 Modify `guild-raid-current-status` season timing: projection allowed, `endsAt` still authored-only.

## 3. Gates

- [ ] 3.1 `dotnet build`, `dotnet test tests/TacticusPlanner.GameCatalog.Tests`, `dotnet test tests/TacticusPlanner.Api.Tests`, `dotnet format --verify-no-changes` (CI).
