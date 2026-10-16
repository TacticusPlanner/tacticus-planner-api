# Tasks

Paths are under `src/TacticusPlanner.GameCatalog` unless stated. Build and test commands: see this repo's AGENTS.md (`dotnet test tests/TacticusPlanner.GameCatalog.Tests`).

## 1. Data and validation

- [x] 1.1 Set `killPoints` to 30, 34 and 43 on the Alpha, Beta and Gamma lanes of `Data/lres/lres-votanuthar.json` (spec: a lane's killPoints is its defeat-all base). Verify: a data test asserts every lane of every embedded event has `killPoints == defeatAll[0]` and that Uthar serves 30 / 34 / 43 (and Lysander 32 / 38 / 31, Fabius Bile 37 / 40 / 46, Farsight 34 / 26 / 17).
- [x] 1.2 Add `ValidateLreLanePoints` in `Validation` (error code `InvalidLaneKillPoints`; fails when `KillPoints != DefeatAll[0]` or `DefeatAll` is empty; the message names the event, source file, lane and both values) and call it from `GameCatalogValidator` beside `ValidateLreRewards`. Verify: unit tests cover the three spec scenarios (Uthar copy error naming `votanUthar`, Alpha, 34 and 30; a consistent file loads; an empty `defeatAll` fails).
- [x] 1.3 Update the manifest snapshot test for the changed `lres` hash, leaving `SchemaVersion` unchanged (content-only change). Verify: the manifest snapshot test passes.

## 2. Documentation and integration

- [x] 2.1 In `.agents/skills/game-catalog-data/SKILL.md` (and the `.claude` copy if kept) note that `killPoints` is the defeat-all base and must equal `defeatAll[0]`, with the Uthar copy error as the example. Verify: the skill states the rule and both copies match.
- [ ] 2.2 Run the full build and tests, then request the `lres` dataset from the local Aspire stack. Verify: `dotnet build` and `dotnet test` succeed and `votanUthar` serves `killPoints` 30 / 34 / 43.
  - Partial (2026-10-16): `dotnet build` Release clean, GameCatalog.Tests 282/282 and Api.Tests 621/621 pass, `dotnet format --verify-no-changes` clean; Persistence.IntegrationTests (37) not run, no Docker. Served values asserted through the loader (`LreLanePointsTests.ServedLanesCarryTheirDefeatAllBase`), not through the Aspire stack; the live `lres` request is still open.

## Workflow follow-up

- Deploy before the apps half; archive both halves together after the apps half is verified on staging.
