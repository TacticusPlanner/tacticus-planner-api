# Tasks

## 1. Endpoint

- [x] 1.1 Add `DeleteProjectEndpoint` (`DELETE projects/{projectId}`): 404 for unknown/foreign, 409 `defaultProjectCannotBeDeleted` for Default, otherwise relocate orphaned goals to Default and delete, under the profile lock.
- [x] 1.2 Regenerate `artifacts/openapi` and verify the new operation.

## 2. Tests

- [x] 2.1 `ProjectsEndpointTests`: orphaned goals land in Default with unchanged priorities; shared goals keep their other memberships; archived project deletes and a repeat delete is 404; Default is rejected; another profile's project is 404.

## 3. Spec

- [x] 3.1 Modify `default-project` "cannot be archived or removed".

## 4. Gates

- [x] 4.1 `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test tests/TacticusPlanner.Api.IntegrationTests`.
- [ ] 4.2 Coordinate the companion `tacticus-planner-apps` change (pull the merged API SHA there).
