## 1. Persistence and migration

- [ ] 1.1 Add Goal global priority and Profile order revision persistence plus account-scoped constraints; verify model tests cover Active/Paused versus historical rows and mixed unit types.
- [ ] 1.2 Add an EF migration with deterministic Current-plan-first backfill, shared-goal deduplication, fallback ordering, and project-priority removal via `dotnet ef migrations add`; verify migration tests for no Current plan, historical goals, empty projects, and cross-project conflicts.
- [ ] 1.3 Add a migration invariant check and documented pre-migration backup/restore procedure; verify a migrated fixture has the same in-flight goal count and one position per goal.

## 2. Global ordering contract

- [ ] 2.1 Implement profile-serialized global order service with revision and collision-safe normalization; verify concurrent reorder/create/status tests and no deadlock with project-slot locks.
- [ ] 2.2 Add `PUT /me/goals/order` full-set validation and structured stale/foreign/duplicate conflicts; verify endpoint authorization, accepted dependency inversion, and atomic rejection tests.
- [ ] 2.3 Update goal creation, combined creation, import, status transitions, and deletion to maintain one global order and advance revision only on set/order changes; verify lifecycle integration tests including pause/resume and terminal reopen.
- [ ] 2.4 Update global and project goal reads to expose canonical positions, retire project-scoped reorder and caller priority, and keep membership/Current plan changes order-neutral; verify endpoint contract tests and last-membership regression tests.
- [ ] 2.5 Regenerate and inspect `artifacts/openapi` for global order/read DTOs and retired project order contract; verify the companion apps change consumes the final shapes.

## 3. Verification

- [ ] 3.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, and `git diff --check`; verify all gates pass.
