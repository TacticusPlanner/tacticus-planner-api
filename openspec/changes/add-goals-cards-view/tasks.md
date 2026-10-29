## 1. Contract and persistence

- [x] 1.1 Add `GoalsView` (default `"Cards"`) and a `SupportedGoalsView` set (`Cards`, `Table`) to `UserSettingsData`. Add a `GoalsView { Cards, Table }` enum in `Features/UserSettings`. Extend `UserSettingsResponse` with a `NormalizeGoalsView` read mapping (missing, null or unsupported reads as `Cards`) and `UpdateUserSettingsRequest` with a `[JsonRequired] GoalsView`. Make the PUT handler persist it alongside `DailyEnergy` and `XpBookRarity`. Verify with `dotnet build TacticusPlanner.slnx -c Release --no-restore`.
- [x] 1.2 Generate the migration with `dotnet ef migrations add AddUserSettingsGoalsView --project src/TacticusPlanner.Persistence --startup-project src/TacticusPlanner.Api`. Verify that the snapshot diff is only the new owned property and that `Up`/`Down` issue no DDL or data change. If they do, stop and raise it; don't accept it.

## 2. Tests and contract

- [x] 2.1 Update `UserSettingsEndpointTests`, and any other test that builds the PUT body, for the new request shape. Add tests for:
  - the `Cards` default on a new profile;
  - persisting `Table` with a revision bump;
  - changing only `goalsView` while keeping `dailyEnergy` and `xpBookRarity`;
  - rejecting `Grid`, a missing value and an empty value with 400 and unchanged state;
  - a stale revision returning 409 with `goalsView` in the conflict body.

  Verify that they pass.
- [x] 2.2 Following `UserSettingsXpBookRarityPostgresTests`, add a Postgres test that seeds legacy `settings` JSON without `GoalsView`, plus one with an unsupported stored value. Confirm GET returns `Cards` with the stored `dailyEnergy` and `xpBookRarity`, and verify the tests pass.
- [x] 2.3 Verify that the regenerated `artifacts/openapi/TacticusPlanner.Api.json` adds only `goalsView` (with its `GoalsView` enum schema) to the user-settings request and response, required on the request. Record the contract for the companion apps change `add-goals-cards-view`: values, the `Cards` default, and that PUT now requires the field.

## 3. Gates

- [x] 3.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` and `dotnet test TacticusPlanner.slnx -c Release --no-build`, and verify that all pass.

## Deferred / out-of-session

- Full-stack save and reload of the Goals view from the apps toggle in the Aspire stack is covered by the companion apps change's manual verification task. Track it there, not here.
