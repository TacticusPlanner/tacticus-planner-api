## 1. Contract and persistence

- [ ] 1.1 Add `XpBookRarity` (default `"Legendary"`) and a supported-values set to `UserSettingsData`; extend `UserSettingsResponse`/`UpdateUserSettingsRequest`, the validator, and the PUT handler to carry and persist it, mapping a missing/null/unsupported stored value to `Legendary` on read; verify with `dotnet build TacticusPlanner.slnx -c Release --no-restore`.
- [ ] 1.2 Generate the migration with `dotnet ef migrations add AddUserSettingsXpBookRarity --project src/TacticusPlanner.Persistence --startup-project src/TacticusPlanner.Api`; verify the snapshot diff is only the new owned property and the migration Up/Down issue no DDL or data change (delete the migration only if EF produces none, and say so).

## 2. Tests and contract

- [ ] 2.1 Update `UserSettingsEndpointTests` and `PlannerDbContextQueryFilterTests` for the new request shape; add tests for the `Legendary` default on a new profile, persisting `Epic` with a revision bump, rejecting `Godly`/missing/empty with 400 and unchanged state, and a stale revision still returning 409; verify `dotnet test` passes for these.
- [ ] 2.2 Add a test that seeds legacy `settings` JSON without the rarity and confirms GET returns `Legendary` with the stored `dailyEnergy`; verify it passes (this is the check on the EF missing-property assumption in design.md).
- [ ] 2.3 Verify the regenerated `artifacts/openapi/TacticusPlanner.Api.json` adds `xpBookRarity` to both the request and response schemas and nothing else changes; note the contract for the companion apps change `surface-goal-farming-guidance` (types, default, PUT now requires the field).

## 3. Gates

- [ ] 3.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all pass.

## Deferred / out-of-session

- Full-stack save/reload of the rarity from the apps dialog in the Aspire stack is covered by the apps change's task 2.4/1.3; track there, not here.
