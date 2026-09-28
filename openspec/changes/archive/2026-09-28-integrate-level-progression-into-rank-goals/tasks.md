## 1. Remove the Level goal type

- [x] 1.1 Remove `GoalType.Level`, `GoalConfig.Level`/`LevelTarget`, and their validation, mapping, EF configuration, and response fields; verify a request naming a Level goal or Level target returns 400 and existing goal reads are unaffected.
- [x] 1.2 Drop Level from combined creation and dependency validation so no goal may depend on a Level goal; verify direct and combined creation for Rank (with and without a higher-than-current level) and Ability targets creates no Level goal or edge.
- [x] 1.3 Drop Level from target editing; verify the target endpoint rejects a Level group and still edits Rank, Ascension, Ability, and Upgrade.

## 2. Migration and import

- [x] 2.1 Add the EF data migration that deletes Level goals and removes their ids from other goals' `depends_on`; verify with an old-data Postgres migration test (Level goal with Rank and Ability dependents, standalone Level, no Level) that dependents survive with clean dependency lists.
- [x] 2.2 Update V1 import so it synthesizes only Unlock and Ascension prerequisites; verify import tests for Rank-only, Ability-only, Rank+Ability, and no-auto-prerequisite paths create no Level goal and keep ordering and shortfall behavior.

## 3. Contract and gates

- [x] 3.1 Build and inspect generated `artifacts/openapi` for the removed Level type/config, coordinate with the paired apps change, and verify contract tests.
- [x] 3.2 Author the remaining delta spec (`goal-target-editing`); `goal-lifecycle-status` needs none in the API (see design).
- [x] 3.3 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all gates pass.
