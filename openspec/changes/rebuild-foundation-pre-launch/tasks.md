# Tasks

Assumptions recorded from design.md Open Questions: `user_settings` stays its own table (no fold); run-record page size defaults to 20, max 100.

## 1. Domain and persistence model

- [ ] 1.1 Merge `Account` into `Profile` (`Issuer`, `Subject`, `LastSeenAt`, unique `(issuer, subject)`), delete `Account`/`AccountId`/`AccountConfiguration`, update `PlannerDbContext`, `CurrentUserPreProcessor`, `GetCurrentUserEndpoint`, `PurgeAccountEndpoint`, `AnalyticsIdentityDeriver`; verify `CurrentUserEndpointTests`, `PurgeAccountEndpointTests`, `AnalyticsIdentityDeriverTests` pass against the merged entity
- [ ] 1.2 `Goal`: remove `Events`/`GoalEvent`/`GoalEventType`/`GoalTargetSnapshot`, add `RankTargetKey` (written from config by the mapper on create/target edit), move the two partial unique indexes to `GoalConfiguration` scoped by `profile_id`; `ProjectGoal` reduced to `(ProjectId, GoalId, CreatedAt)`; verify `RankTargetKeyTests` pass and a Persistence integration test proves both partial indexes reject duplicates and accept distinct Rank targets
- [ ] 1.3 `LegendaryEventPlan`: replace `Teams`/`Runs`/`Annotations` entity collections with owned-JSON collections (`teams`, `runs`, `annotations` jsonb; one `LegendaryEventTeamRecord` type shared with run records), delete the six child entities/configurations/DbSets and their query filters; verify a Persistence integration test round-trips a plan with 3 lanes × 2 teams × members/objectives/run depths + runs + annotations
- [ ] 1.4 Fold `RosterSnapshot` into `LegendaryEventRunRecord.Roster` (owned JSON `{takenAt, catalogVersion, characters, mows}`), delete the entity/table/`RosterSnapshotId`; fold `GuildRaidAttackUnit` into `GuildRaidAttack.Units` (owned JSON), delete the entity/table; verify `GuildRaidPersistencePostgresTests` and the LE run-record Postgres tests pass on the new shapes
- [ ] 1.5 Drop `PlayerDataOverride.BattleResultOverrides`, `PlayerDataSnapshot.TacticusLastUpdatedOn`, `created_at`/`updated_at` from `project_goals` (keep `created_at`) and `guild_raid_attacks`, the `ix_projects_profile_id` index, and the CHECK constraints on `goals.status/goal_type/entity_type` and `projects.*`; move those enum sets into `GoalValidation`/`ProjectValidation` and `LegendaryEventValidation` (lane, position, run, depth); verify `EntityMetadataInterceptor` no longer expects timestamps on non-`BaseEntity` rows and the validators reject every value the constraints used to reject (unit tests)
- [ ] 1.6 Delete `src/TacticusPlanner.Persistence/Migrations/*`, run `dotnet ef migrations add InitialCreate --project src/TacticusPlanner.Persistence --startup-project src/TacticusPlanner.Api`, append the raw `ALTER TABLE goals ADD CONSTRAINT ix_goals_profile_global_priority UNIQUE (profile_id, global_priority) DEFERRABLE INITIALLY DEFERRED;` to its `Up` with a pointer comment in `GoalConfiguration`; delete `AddGlobalGoalPriorityMigrationTests`, `GoalAcquisitionSourcesMigrationTests`, `LegendaryEventPlansMigrationPostgresTests`, `LegendaryEventRunRecordsMigrationPostgresTests`, `RemoveActiveProjectMigrationTests`, `RemoveLevelGoalsMigrationTests`; add a Persistence test asserting the deferrable constraint exists after migration; verify `dotnet ef migrations list` shows exactly one migration and the Docker-backed suite migrates a fresh database

## 2. Goal and project write paths

- [ ] 2.1 Replace `GoalConflictDetection`/`SyncOccupancyAsync`/per-project `FOR UPDATE` in `ProjectGoalPlanningService` with one profile lock and one `goals` query for slot conflicts; verify `ProjectGoalConcurrencyPostgresTests`/`RankTargetSlotsPostgresTests` rewritten for profile scope pass (racing same-slot creates → one `slotOccupied`; distinct slots all commit; order contiguous)
- [ ] 2.2 `POST /me/goals` takes the combined request (single = one spec); delete `CreateGoalEndpoint`, `UpdateGoalEndpoint`, `UpdateGoalTargetEndpoint`, `UpdateGoalProjectsEndpoint`, `UpdateGoalStatusEndpoint`, `UpdateProjectGoalsStatusEndpoint`, `ListProjectGoalsEndpoint`; `EditGoalEndpoint` becomes `PUT /me/goals/{goalId}`; verify `GoalEditEndpointTests`, `GoalTargetEditEndpointTests`, `CreateCombinedGoalsEndpointTests` pass on the new routes and the removed routes return 404 in a routing test
- [ ] 2.3 Add `POST /me/goals/status` (`UpdateGoalsStatusEndpoint`) per `goal-bulk-status`: atomic, profile-locked, `expectedOrderRevision` when the in-flight set changes, `{goals, orderRevision}` response; verify endpoint tests for the three requirements' scenarios plus `GoalStatusInvarianceTests` migrated to the bulk route
- [ ] 2.4 `ListGoalsEndpoint` returns full detail with `projectIds` from one grouped `project_goals` query; delete `GoalSummaryResponse`/`ToSummary`; `ListProjectsEndpoint` adds `goalIds` in global order; `UpdateProjectGoalsEndpoint` drops slot checks; verify `GoalsEndpointTests`/`ProjectsEndpointTests`/`ProjectMembershipReplacementTests` cover list-equals-detail, `goalIds` ordering and the two-Rank-targets membership scenario
- [ ] 2.5 `DeleteGoalEndpoint` removes the deleted id from every dependent's `DependsOn` in the same transaction; verify a test deletes an Unlock goal and reads its Ascension dependent with an empty `dependsOn`

## 3. Legendary events and guild raids

- [ ] 3.1 Rewrite `LegendaryEventPlanWriter`/`LegendaryEventTeamMutations`/`LegendaryEventPlanInputMutations` as in-memory edits of the loaded aggregate + one `SaveChanges` under the `Revision` token (no intermediate save, no `AsSplitQuery`), invariants asserted by `LegendaryEventValidation` before save; verify `LegendaryEventPlanEndpointTests`, `LegendaryEventRunInputsEndpointTests`, `LegendaryEventPlanProjectionTests` pass unchanged except for the 409 body shape, and the Postgres concurrent-first-writers test passes
- [ ] 3.2 `LegendaryEventRunRecordRecorder` writes the inline roster; `GetLegendaryEventRunRecordsEndpoint` paged (`after`, `limit` 1..100, default 20, `nextAfter`) without roster; add `GET …/run-records/{recordId}/roster` with ETag/304; verify `LegendaryEventRunRecordEndpointTests` cover paging (25 records → 20 + 5, no duplicates), invalid limit, roster read, 304, foreign record 404
- [ ] 3.3 `GuildRaidStatusService` persists attack units inline and the projector/read path ignores them; verify `GetMyGuildRaidStatusEndpointTests`, `GuildRaidStatusProjectorTests`, `GuildRaidPersistencePostgresTests` pass and repeated refresh stays idempotent on `content_hash`

## 4. HTTP contract

- [ ] 4.1 Register `ProblemDetails` for all errors: FastEndpoints `ErrorResponseBuilder` → `application/problem+json` with `issueCode`/`errors`; `GlobalExceptionHandler` maps concurrency → 409 `concurrency`, unhandled → 500 `internal`; replace the six bespoke 409 records with a shared `ConflictProblem(issueCode, current)` helper; verify a test per `api-error-contract` scenario and every existing 409/400 test asserts the new shape
- [ ] 4.2 Move `POST tacticus-integration/player-sync` → `POST me/player-data/sync`; delete `ValidateTacticusApiKeyEndpoint`; verify `PlayerDataSyncEndpointTests` on the new route and `TacticusApiRegistrationTests` adjusted
- [ ] 4.3 Manifest dataset `url`s carry the dataset hash (`game-catalog/{dataset}/{hash}`), handler validates the hash (404 on mismatch) and sets `Cache-Control: public, max-age=31536000, immutable`; the unhashed route keeps `must-revalidate`; verify `GameCatalogSnapshotTests` manifest snapshot updated and tests for hashed-200/immutable, mismatched-hash-404
- [ ] 4.4 Build and verify the regenerated `artifacts/openapi/TacticusPlanner.Api.json`: removed routes absent, `goals` list schema equals detail schema, `goalIds` on projects, `POST me/goals/status`, paged run records + roster route, problem-details responses on every error; coordinate the schema with the companion apps change (`rebuild-foundation-pre-launch` in `tacticus-planner-apps`)

## 5. Docs and environments

- [ ] 5.1 In `tacticus-planner-docs`: mark ADR 0001 Superseded (single `public` schema with table prefixes), rewrite `architecture/data.md` "Relational and JSONB Boundary" + "Current State and Domain History" to match ADR 0002/0007 and this change, add ADR 0011 "Aggregate-as-jsonb with validator-enforced invariants"; verify the docs repo's own lint/link check passes
- [ ] 5.2 Update `README.md` (routes table, migration reset procedure) and `AGENTS.md` migration section (single `InitialCreate`, the raw constraint rule); verify `aspire run` + `api-migrations` Reset Database brings up a fresh database and `GET me` provisions a profile

## 6. Verification

- [ ] 6.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build` green (Docker running for the Persistence suite); `docker build -f src/TacticusPlanner.Api/Dockerfile .` succeeds
- [ ] 6.2 Command-count ceilings (from `optimize-api-handlers` 1.1, or added here if that change has not landed): `GET me/goals` ≤ 3, `POST me/goals` (3 specs) ≤ 6, `POST me/goals/status` ≤ 5, `PUT me/goals/{id}` ≤ 6, LE plan mutation ≤ 3; verify the assertions pass and record the numbers in this file

## Deferred / out-of-session

- Staging/production database drop + recreate via `tacticus-planner-infra` when the API image is deployed (tracking issue to be opened in `tacticus-planner-infra`).

## Workflow follow-up

- Apply order: this change, then `rebuild-foundation-pre-launch` in `tacticus-planner-apps`.
- Both PRs green and review threads resolved before sync/archive of either half.
