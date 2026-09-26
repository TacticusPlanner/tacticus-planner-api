## 1. Persistence and migration

- [x] 1.1 Add Goal global priority and Profile order revision persistence plus account-scoped constraints; verify model tests cover Active/Paused versus historical rows and mixed unit types.
- [x] 1.2 Add an EF migration with deterministic Current-plan-first backfill, shared-goal deduplication, fallback ordering, and project-priority removal via `dotnet ef migrations add`; verify migration tests for no Current plan, historical goals, empty projects, and cross-project conflicts.
- [x] 1.3 Add a migration invariant check and documented pre-migration backup/restore procedure; verify a migrated fixture has the same in-flight goal count and one position per goal.

## 2. Global ordering contract

- [x] 2.1 Implement profile-serialized global order service with revision and collision-safe normalization; verify concurrent reorder/create/status tests and no deadlock with project-slot locks.
- [x] 2.2 Add `PUT /me/goals/order` full-set validation and structured stale/foreign/duplicate conflicts; verify endpoint authorization, accepted dependency inversion, and atomic rejection tests.
- [x] 2.2a Re-contract `PUT /me/projects/{id}/goal-order` as the subset move `{goalId, displacedGoalId, expectedRevision}` with the array-move rule from the design; verify the A,B,C,D,E / project A,C,E example in both directions, hidden non-member goals keeping their relative order, non-member/non-in-flight/foreign/self/stale rejections, shared revision with the global reorder, and no membership or dependency change.
- [x] 2.3 Update goal creation, combined creation, import, status transitions, and deletion to maintain one global order and advance revision only on set/order changes; verify lifecycle integration tests including pause/resume and terminal reopen.
- [x] 2.4 Update global and project goal reads to expose canonical positions, re-contract project-scoped reorder as the subset move, retire caller priority, and keep membership/Current plan changes order-neutral; verify endpoint contract tests and last-membership regression tests.
- [ ] 2.5 Regenerate and inspect `artifacts/openapi` for global order/read DTOs and the re-contracted project move contract and retired full-order body; verify the companion apps change consumes the final shapes.

## 3. Verification

- [x] 3.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, and `git diff --check`; verify all gates pass.
