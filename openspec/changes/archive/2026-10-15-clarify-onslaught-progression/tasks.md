# Tasks

Companion apps change: `tacticus-planner-apps/openspec/changes/clarify-onslaught-progression`. Apply this API change first.

## 1. Contract

- [x] 1.1 Add `bool IsSet` to `OnslaughtAllianceProgressResponse`; derive it from the presence of the alliance's override record (Stone 1 placeholder when absent).
- [x] 1.2 Make the PUT request's alliance members nullable (null = not set, no record stored); validate only non-null alliances.
- [x] 1.3 Update `artifacts/openapi/TacticusPlanner.Api.json` with the required `isSet` boolean and the nullable request alliances and coordinate the companion apps change.

## 2. Tests

- [x] 2.1 `OnslaughtProgressEndpointTests`: unset defaults report `isSet: false`; PUT and a reload report `isSet: true`; a saved Stone 1 reports `isSet: true`; a null alliance stays unset.
- [x] 2.2 `V1ImportEndpointTests`: imported and preserved alliances report `isSet: true`.

## 3. Gates

- [ ] 3.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` (regenerates and confirms the OpenAPI artifact), and `dotnet test TacticusPlanner.slnx -c Release --no-build`.

## Deferred / out-of-session

- 3.1 could not run in the authoring session: no .NET SDK was available in the container. Run it before archive.
