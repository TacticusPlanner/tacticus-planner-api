## 1. Verify and change creation semantics

- [ ] 1.1 Reproduce a current Rank/Level pair and a shared Ability prerequisite through the Aspire stack; verify fixtures capture present goal responses and import outcomes.
- [ ] 1.2 Align Rank validation/combined creation with an intrinsic level gate and no required Level edge solely for Rank; verify API tests for direct and combined creation, rank target changes, and standalone Level creation.
- [ ] 1.3 Update V1 import to synthesize Level only for eligible non-Rank prerequisites while retaining Unlock/Ascension rules; verify import tests for Rank-only, Ability-only, shared Ability, and no-auto-prerequisite paths.

## 2. Legacy and contract verification

- [ ] 2.1 Implement non-destructive interpretation of Rank-only legacy Level links and preserved shared Ability links; verify stored Level ids remain readable and no duplicate XP need is exposed to the apps companion.
- [ ] 2.2 Build and inspect generated `artifacts/openapi` for any changed endpoint/outcome contract, coordinate with paired apps change, and verify contract tests.
- [ ] 2.3 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all gates pass.
