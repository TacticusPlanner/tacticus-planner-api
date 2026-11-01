## Why

Users can only archive a project, so old event projects pile up in the archived section forever. A UserJot request ("Enable Permanent Project Deletion") asks for a real delete. Goals already have a hard delete; projects should too.

Companion apps change: `add-project-deletion` in `tacticus-planner-apps` (Delete action in the project row menu and a confirmation dialog). This API half applies first.

## What Changes

- Add `DELETE /api/v1/projects/{projectId}`: permanently deletes a custom project, archived or not. Responds 204.
- Goals are never deleted. The project's memberships cascade away; any goal whose only membership was this project is filed into the Default project in the same transaction, so every goal keeps at least one membership. Goal status, target and global priority are unchanged.
- The Default project cannot be deleted: 409 `defaultProjectCannotBeDeleted` with the project under `current`.
- Runs under the profile lock, like every membership change.
- The regenerated OpenAPI artifact gains the new operation.

## Capabilities

### Modified Capabilities

- `default-project`: the Default project is protected from deletion with an explicit issue code, and is where a deleted project's otherwise-orphaned goals land.

## Impact

- `src/TacticusPlanner.Api/Features/Projects/DeleteProjectEndpoint.cs` (new).
- `artifacts/openapi`: new `delete` operation on `/api/v1/projects/{projectId}`.
- Tests: `ProjectsEndpointTests` (relocation, archived delete + repeat 404, Default rejected, cross-profile 404).
- No database or migration changes (`project_goals.project_id` already cascades).
