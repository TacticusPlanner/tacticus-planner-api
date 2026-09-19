## 1. Remove membership from the creation-status decision

- [x] 1.1 Add `bool StartPaused = false` as a trailing optional parameter of `CreateGoalRequest` (`Features/Goals/CreateGoalEndpoint.cs`), and replace the `targetProjects.Any(p => p.Id == profile.ActiveProjectId) ? Active : Paused` assignment at `CreateGoalEndpoint.cs:91` with `req.StartPaused ? GoalStatus.Paused : GoalStatus.Active`. Line 91 is the **only** use of the `profile` read at `CreateGoalEndpoint.cs:65`, so delete that read too — `EnsureDefaultProjectAsync` loads its own profile, and `TreatWarningsAsErrors` in `Directory.Build.props` turns a leftover unused local into a build failure. Verify by creating a goal into a non-current project and observing `Active` in the response.
- [x] 1.2 Add the same `bool StartPaused = false` parameter to `CreateCombinedGoalsRequest` and replace the status derivation at `CreateCombinedGoalsEndpoint.cs:145` the same way, applying one status to every goal in the request. As in 1.1, line 145 is the only use of the `profile` read at `CreateCombinedGoalsEndpoint.cs:122` — delete it, keeping the slot-uniqueness comment that sits above it. Verify a combined request into a non-current project returns every goal `Active`.
- [x] 1.3 Replace the status derivation at `V1GoalImportService.cs:365` with V1's own per-goal planning choice and drop the now-unused `profile` read. Widen `V1Goal.DailyRaids` to `bool?` (absent imports `Active`), carry it on `TranslatedGoal` as `InDailyPlanning`, and map it per goal to `Active`/`Paused`. A merged duplicate survivor takes `Active` if any duplicate had it; a synthesized prerequisite takes `Active` if any of its unit's candidates is `Active`. Verify an import run while another project is the active plan honours each source goal's flag.
- [x] 1.4 Confirm no call site derives a goal status from `Profile.ActiveProjectId`. `rg "ActiveProjectId" src` should leave exactly these, none of them status-related: `ActivateProjectEndpoint` (writes it), `ProjectsService` (`??=` initialization), `GetCurrentUserEndpoint`, `CreateProjectEndpoint:53` and `ListProjectsEndpoint:49` (both pass it to `Map.ToSummary` for the Current-plan marker), `ProjectMapper` (consumes it there), and `UpdateProjectEndpoint`'s archive guard.

## 2. Correct the documented contract

- [x] 2.1 Update `CreateGoalEndpoint`'s `Summary.Description`, which currently states "The goal starts Active if any target project is the caller's active plan, otherwise Paused", to describe the new rule and the `startPaused` flag; verify the text in the regenerated OpenAPI artifact.
- [x] 2.2 Update `CreateCombinedGoalsEndpoint`'s `Summary.Description` the same way, including that `startPaused` applies to the whole request; verify in the regenerated artifact.
- [x] 2.3 Update the `CreateGoalEndpoint` class-level XML doc comment, which describes project membership as deciding the goal's starting status, to match.
- [x] 2.4 Build and confirm `artifacts/openapi` regenerates with `startPaused` present and defaulted on both creation request schemas, and coordinate the regenerated contract with the companion `clarify-project-purpose` change in `tacticus-planner-apps`.

## 3. Tests

- [x] 3.1 Rewrite `GoalsEndpointTests.CreateGoalInNonActiveProjectStartsPaused` (`tests/TacticusPlanner.Api.Tests/GoalsEndpointTests.cs:185`) into a test asserting the goal is created `Active`, and rename it accordingly; verify it fails against the old implementation and passes against the new one.
- [x] 3.2 Rewrite `CreateCombinedGoalsEndpointTests.CreateInNonActiveProjectStartsAllGoalsPaused` (`tests/TacticusPlanner.Api.Tests/CreateCombinedGoalsEndpointTests.cs:143`) the same way, asserting every returned goal is `Active`.
- [x] 3.3 Add coverage for `startPaused: true` on `POST /me/goals` (goal is `Paused`) and for the flag omitted (goal is `Active`).
- [x] 3.4 Add coverage for `startPaused: true` on `POST /me/goals/combined` asserting every goal in the returned set, prerequisites in the dependency chain included, is `Paused`.
- [x] 3.5 Add a test that creating a `startPaused` goal whose `(entityType, entityId, goalType)` slot is already held by an `Active` goal in the target project returns 409 `projectGoalSlotOccupied` — `Paused` occupies a slot exactly as `Active` does.
- [x] 3.6 Add V1 import coverage (`tests/TacticusPlanner.Api.Tests/V1GoalImportEndpointTests.cs`) that an import performed while a non-default project is the active plan honours each source goal's `dailyRaids` flag: a flagged goal imports `Active`, an unflagged one `Paused`, a goal with no flag at all `Active`, a synthesized prerequisite follows its unit's candidates, and a merged duplicate keeps an `Active` choice.
- [x] 3.7 Add regression coverage for "Project operations do not change a goal's status": making a project current leaves its `Paused` goals paused, losing current-plan standing leaves `Active` goals active, and adding or removing a membership (both `PUT /me/goals/{id}/projects` and `PUT /me/projects/{id}/goals`) leaves status unchanged. This codifies `GP-25`'s constraint, which currently holds only by accident.
- [x] 3.8 Re-run the existing project and goal suites and confirm nothing else depended on membership-derived status — in particular `ProjectsEndpointTests` and `ProjectGoalPostgresTests`.

## 4. Gates

- [x] 4.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
- [x] 4.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore`
- [x] 4.3 `dotnet test TacticusPlanner.slnx -c Release --no-build`

## Deferred / out-of-session

- [ ] 5.1 Full-stack verification that a goal created into a non-current project is `Active` and absent from the Current plan's Dailies — requires the companion `tacticus-planner-apps` change to be applied, and belongs to that change's manual-verification tasks rather than this one.
