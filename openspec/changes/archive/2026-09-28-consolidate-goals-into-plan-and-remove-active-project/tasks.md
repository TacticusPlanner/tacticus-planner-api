## 1. Persistence and migration

- [x] 1.1 Remove `Profile.ActiveProjectId` and its mapping in `ProfileConfiguration`; add the partial unique index `(profile_id) WHERE type = 'Default'` to the Project configuration; verify the model builds and the snapshot has no unrelated drift.
- [x] 1.2 Add an EF migration after `AddGlobalGoalPriority` via `dotnet ef migrations add`: backfill a missing Default project per profile, demote extra Default rows (oldest kept) to Custom, create the unique index, drop `profiles.active_project_id`; `Down` re-adds the column pointing at each Default project; verify migration tests for no Default project, duplicate Defaults, non-default active project, and unchanged goal/membership counts.

## 2. API behaviour

- [x] 2.1 Delete `ActivateProjectEndpoint`; remove `IsActivePlan` and the `activeProjectId` parameter from `ProjectMapper`; verify the route is gone and project summaries have no active-plan flag.
- [x] 2.2 Remove the `activeProjectCannotBeArchived` rule from `UpdateProjectEndpoint`, keep `defaultProjectCannotBeArchived`, and stop loading the profile in `UpdateProjectEndpoint`, `CreateProjectEndpoint` and `ListProjectsEndpoint`; verify a formerly-active custom project can be archived and the Default project cannot.
- [x] 2.3 Simplify `ProjectsService.EnsureDefaultProjectAsync` (no profile load or pointer), handle a unique-index violation by re-reading, and stop seeding the pointer in `GetCurrentUserEndpoint`; update doc comments in `Project.cs` and `Profile.cs`; verify concurrent ensure produces exactly one Default project (Postgres test).
- [x] 2.4 Regenerate and inspect `artifacts/openapi` (activate path, `isActivePlan`, required list, any documented `activeProjectCannotBeArchived`); verify the companion apps change consumes the final shapes.

## 3. Tests

- [x] 3.1 Update ProjectsEndpointTests (activate flow, single-active assertion, archive-active case), GoalStatusInvarianceTests, V1GoalImportEndpointTests, GoalsEndpointTests and CreateCombinedGoalsEndpointTests (rename the "NonActiveProject" cases, drop `IsActivePlan` assertions), and check `ProjectGoalConcurrencyPostgresTests`; verify the suites pass.

## 4. Docs and specs

- [ ] 4.1 Rewrite the Current plan claims (about lines 144-163) in `tacticus-planner-docs/community/early-testing-guide.md` to match the paired apps behaviour; verify no remaining "Current plan" wording there.
- [ ] 4.2 Confirm `establish-global-goal-priority` is archived before this change and that its superseded "Current plan remains a browsing preference" wording is not carried into main specs; verify `openspec validate --strict` for both changes.

## 5. Verification

- [x] 5.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, and `git diff --check`; verify all gates pass.
