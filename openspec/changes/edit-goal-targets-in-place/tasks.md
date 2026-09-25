## 1. Contract and validation

- [x] 1.1 Add `revision` to goal detail and a dedicated target-update request/response for Rank, Ascension, Level, Ability, and Upgrade; verify schema/mapper tests and old goal JSON reads.
- [x] 1.2 Reuse creation-equivalent catalog/dependency validation against persisted baseline, permit already-attained valid targets, and reject Unlock/historical states; verify endpoint tests for every supported kind and malformed mixed payloads.

## 2. Atomic target mutation

- [x] 2.1 Apply expected-revision checks, Rank occupancy across every membership, target/key update, and TargetChanged event in one transaction; verify concurrent edit, multi-project conflict, and no-op retry tests.
- [x] 2.2 Preserve goal id, creation snapshot, event history, status, notes, strategy, memberships, and priority; verify before/after endpoint tests and unchanged non-target fields.
- [x] 2.3 Confirm EF JSON-event model requires no relational schema migration; if a schema change is generated, add its EF migration in this change and verify an old-data migration test.

## 3. Handoff and gates

- [x] 3.1 Regenerate and inspect `artifacts/openapi` for target PUT, revision, history, and 409 shapes; coordinate consumer types with paired apps change and verify contract tests.
- [ ] 3.2 Through Aspire, verify a Rank target edit, same-project conflict, two-tab stale revision, and an already-reached target against fresh API reads.
- [x] 3.3 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all gates pass.
