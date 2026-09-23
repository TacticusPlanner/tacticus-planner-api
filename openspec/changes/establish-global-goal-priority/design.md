## Context

`ProjectGoal.Priority` currently stores the executable order, `ListProjectGoalsEndpoint` and `UpdateProjectGoalOrderEndpoint` expose it, and several creation/status/membership paths normalize it per project. `Profile.ActiveProjectId` denotes Current plan. The paired apps change is `tacticus-planner-apps/openspec/changes/establish-global-goal-priority` and consumes goal list, project list, and reorder contracts.

## Goals / Non-Goals

**Goals:** Exactly one durable account-wide in-flight order, deterministic migration, and atomic concurrency behavior.

**Non-Goals:** Remove projects, allow zero-membership goals as a public workflow, alter dependencies or goal statuses, or change game catalog data.

## Decisions

1. Add nullable `Goal.GlobalPriority` for in-flight position and `Profile.GoalOrderRevision` for optimistic concurrency. Enforce a per-profile partial uniqueness constraint over in-flight positions and a non-null/positive invariant for Active/Paused goals. Historical goals have null position. Avoid deriving order from membership rows because shared goals would still have multiple positions.
2. Use a dedicated `PUT /me/goals/order` with full goal ID set and expected revision. A sparse move endpoint was considered, but a full set makes stale membership detectable and avoids ambiguous placement among hidden goals. Return current revision and canonical ordered IDs on success; return structured conflict details on stale set/revision.
3. Serialize every mutation of the account's in-flight order on the owning Profile row inside the same transaction, then normalize priorities and increment revision only when the set/order changes. This covers create, combined create, status transitions, delete, and reorder. Membership replacement does not touch it. A unique index plus temporary-gap/two-phase assignment prevents transient uniqueness collisions while permuting. Existing project-slot locking remains for membership conflicts; acquire profile lock before project locks on paths needing both, with tests for deadlock freedom.
4. Remove per-project priority from the writable model and retire `PUT /me/projects/{id}/goals/order` after companion apps clients switch. Project reads join membership with Goal, order by `GlobalPriority`, then by creation time and goal ID for historical goals. Preserve the last-membership invariant and Default fallback; zero membership is not introduced. Current plan remains `ActiveProjectId` for browsing defaults only, never a planning input.
5. Data migration materializes each profile's global order once from `ProjectGoal.Priority` before dropping that column: former Current plan first, then other projects by creation time/ID, each by stored priority/goal ID, deduplicating by goal ID; orphaned in-flight goals append by creation time/ID. Historical goals retain status/history and no in-flight position. Use a transaction and migration tests with shared memberships, no active project, empty projects, and mixed unit types. Do not use `UpdatedAt`.
6. Keep one canonical ordered goal DTO/result for `GET /me/goals`; project responses filter it rather than calculate their own priority. OpenAPI regeneration and apps types are part of the paired contract rollout. No raw or served catalog dataset, denormalization, manifest, or catalog snapshot changes.

## Risks / Trade-offs

- [Automatic EF migrations can run before the new apps deploy] → Deploy API with read compatibility where feasible, coordinate the breaking reorder cutover and apps release, and test both API contracts before retiring the old endpoint.
- [Profile locks add contention] → Keep transactions short; order changes are infrequent compared with reads.
- [Existing project orders disagree] → Migration policy is explicit and deterministic, and tests pin representative conflicts; users may reorder after rollout.
- [A goal can exceed the old per-project priority cap] → Replace project cap with an account-level numeric range suitable for the total goal count and validate overflow.
- [Rollback after dropping old priority] → Take a verified database backup before migration; prefer forward repair, and require backup restore for rollback to an old binary.

## Migration Plan

Apply the apps/API pair in API-first order. Back up the database, then apply the schema addition/backfill and drop the project-priority column in one EF migration before enabling global writes. Verify per-profile ordered-set invariants and counts, then switch reads/writes and remove the old priority endpoint in this change. Regenerate OpenAPI, deploy compatible apps, and monitor 409/conflict and order-validation errors. If cutover fails after migration, prefer forward repair; restoring an old binary requires restoring the verified pre-migration backup. Never recompute from timestamps.
