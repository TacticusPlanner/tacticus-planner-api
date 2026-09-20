## 1. `ProjectGoalPlanningService` rewrite

- [x] 1.1 Remove `OrderGoals` (the within-unit topological sort) entirely and verify no remaining caller references it (`dotnet build` clean)
- [x] 1.2 Rewrite `NormalizeAsync` to drop the `GroupBy(UnitKey.From)` grouping while keeping its full two-zone renumbering pass: in-flight (Active/Paused) memberships renumbered 1..N preserving existing relative order (freshly created ones appended at the end), then historical (Completed/Archived) memberships renumbered N+1..M in stable prior order — verify with unit tests covering (a) a new goal for an existing unit appends at the end rather than joining that unit's old block position, and (b) a goal transitioning Active→Archived (or Archived→Active) lands in the correct zone's numeric range after the next `NormalizeAsync` call, with no interleaving between zones
- [x] 1.3 Replace `ApplyUnitOrderAsync` with `ApplyGoalOrderAsync(ProjectId, IReadOnlyList<GoalId>)`: validate the submitted goal id list is exactly the project's current in-flight (Active/Paused) goal id set (same size/membership check `ApplyUnitOrderAsync` did for units, now at goal granularity), assign sequential priorities in the submitted order when valid, return `false` on any mismatch — verify with unit tests covering an exact match, a missing id, an extra id, and a duplicate id
- [x] 1.4 Verify `ApplyGoalOrderAsync` accepts an order that places a goal ahead of a `DependsOn` prerequisite it hasn't reached (no validation added for this — confirms the design's "no automatic dependency validation on reorder" decision) with a unit test
- [x] 1.5 Update `UpdateProjectGoalsEndpoint`: remove `Priority` handling from `ProjectGoalEntryRequest` (or ignore it if kept for wire-compat) — an existing member's priority is left untouched, a newly added member is appended at the end of the in-flight zone before `NormalizeAsync` runs — verify with unit tests asserting an existing member's priority is unchanged regardless of what's submitted, and a new member lands after all current in-flight goals

## 2. Endpoint replacement

- [x] 2.1 Add `PUT /me/projects/{id}/goal-order` (new endpoint class, request record holding the ordered `List<Guid>` of goal ids) calling `ApplyGoalOrderAsync` through `ExecuteLockedMutationAsync`, returning 200 on success and the existing stale-order 409/400 shape on rejection — mirror `UpdateProjectUnitOrderEndpoint`'s existing structure
- [x] 2.2 Remove `UpdateProjectUnitOrderEndpoint` and its request/response DTOs
- [x] 2.3 Update or remove any test referencing the removed unit-order endpoint or `UnitOrderEntryRequest`, and add endpoint-level tests for the new goal-order endpoint covering success, stale-set rejection, and concurrent-reorder-plus-create (per `project-goal-slots`' existing concurrency scenarios) not regressing

## 3. Verify no hidden unit-contiguity assumption

- [x] 3.1 Grep the api repo for any other reader of `db.ProjectGoals` ordered by `Priority` that assumes contiguous unit blocks (beyond what's already covered by `1.1`-`1.5`) and confirm none exists, or fix any found — record the search performed. Found: `V1GoalImportService.cs`'s own `unitOrder`/`byUnit` grouping — addressed in task group 4. No other reader found (`GuildRaidAttackRepository.cs`'s `GroupBy` is unrelated — guild raid attacks, not goal priority).
- [x] 3.2 Verify `ListProjectGoalsEndpoint`'s plain `OrderBy(Priority)` still correctly keeps historical goals after every in-flight goal, given `1.2`'s two-zone renumbering — integration test covering a project with both in-flight and historical (Archived) goals, asserting the returned order never interleaves them

## 4. V1 import

- [x] 4.1 Update `V1GoalImportService` to construct the import batch in exact V1 priority order (not grouped by unit), placing each automatically created prerequisite immediately before the goal(s) that required it — verify with a unit test covering an interleaved V1 sequence (e.g. character A, character B, character A) importing with that exact interleaving preserved. Also revised: the unit's own imported Ascension goal (`ownAscension`) is no longer pulled to the front of its unit's block — it now keeps its own natural V1 position, since only *synthesized* prerequisites (which have no V1 position of their own) need "immediately before its earliest dependent" placement.
- [x] 4.2 Verify a created prerequisite's placement with a unit test: a prerequisite created for a goal that isn't first in V1's sequence lands immediately before that goal, ahead of earlier, unrelated V1 entries

## 5. Contract and coordination

- [x] 5.1 Verify the regenerated `artifacts/openapi` artifact reflects the removed unit-order endpoint, the new goal-order endpoint, and the changed `UpdateProjectGoalsEndpoint` request shape
- [x] 5.2 Confirm the companion `tacticus-planner-apps` change (`add-inline-goal-reprioritize`) is ready to consume the new endpoint and has stopped sending `Priority` to `UpdateProjectGoalsEndpoint`'s caller before this is applied — coordinate deploy order per design.md's Migration Plan. Apps side complete (all its own tasks done, gates green) and verified live against this api change's actual endpoints (goal-order PUT round-trips succeed); `add-goals-to-project-sheet.tsx` no longer sends `priority`.

## 6. Repository gates

- [x] 6.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
- [x] 6.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore`
- [x] 6.3 `dotnet test TacticusPlanner.slnx -c Release --no-build`
