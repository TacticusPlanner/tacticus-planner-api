## Why

With one account-wide goal order (`establish-global-goal-priority`), the "Active project" / Current plan pointer no longer drives any planning decision: the API reads it only to seed it and to block archiving. Keeping it as a "browsing preference" leaves a dead concept — an `activate` endpoint, an `isActivePlan` flag, an archive guard, and a nullable loose-pointer column — that every client must model and explain. Removing it leaves one simple rule: the Default project is the unremovable filing fallback and every other project is an ordinary project.

## What Changes

- **BREAKING**: Remove `Profile.ActiveProjectId`, `POST /me/projects/{projectId}/activate`, and `isActivePlan` on project summaries; drop the `activeProjectCannotBeArchived` conflict. A project can be archived unless it is the Default project.
- Keep custom projects unchanged: create, rename, edit, pause and archive continue to work. There is still no project delete endpoint.
- Make "exactly one Default project per profile" a database-enforced invariant: backfill a Default project for any profile without one, then add a partial unique index on `(profile_id) WHERE type = 'Default'`. The Default project stays renamable and editable, cannot be archived, and remains the last-membership fallback.
- Stop loading the profile in project list/create/update endpoints and in `EnsureDefaultProjectAsync` (they did so only for the active pointer); account provisioning stops seeding the pointer.
- Regenerate the OpenAPI artifact; update tests and the two affected main specs; rewrite the Current plan claims in the docs repo's early-testing guide.
- The Goals/Plan page merge (Global Plan folded into Goals), the `goals/*` to `plan/*` route rename, the new default landing, and every client-side removal of Current plan are **apps-only**; this API change carries none of them.

## Capabilities

### New Capabilities

- `default-project`: Exactly one undeletable, non-archivable, renamable Default project per profile, used as the filing fallback; no active-project selection is exposed.

### Modified Capabilities

- `goal-lifecycle-status`: Creation status, V1 import status and project operations no longer reference an active plan.
- `v1-goal-import`: Imported goals are filed into the Default project; the active plan is no longer a concept to disclaim.

## Impact

- Companion apps change with the same name: `tacticus-planner-apps/openspec/changes/consolidate-goals-into-plan-and-remove-active-project`; apply API first, then release apps that no longer read `isActivePlan` or call `activate`.
- Ordering dependency: `establish-global-goal-priority` must be applied and archived first. Its "Current plan remains a browsing preference" statement (proposal/design) is superseded here; its migration history ("former Current plan first") stays valid because that backfill runs before this change drops the column.
- Affects Profile/Project persistence and configuration, a new EF migration after `AddGlobalGoalPriority`, project/current-user endpoints, `ProjectsService`, the OpenAPI artifact, and tests in ProjectsEndpointTests, GoalsEndpointTests, CreateCombinedGoalsEndpointTests, GoalStatusInvarianceTests, V1GoalImportEndpointTests and the Postgres concurrency suite.
- Docs repo: `tacticus-planner-docs/community/early-testing-guide.md` (Current plan claims around lines 144-163) needs a rewrite.
- V2 is greenfield and pre-production, so the removal is clean (no deprecation shim), per the destructive-changes policy.
