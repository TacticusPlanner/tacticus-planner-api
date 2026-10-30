# Tasks

## 1. CI, stage deploy and image

- [x] 1.1 Pin every third-party action in `_build-api.yml`, `cd-stage.yml` and `coverage.yml` to a commit SHA, with a version comment.
- [x] 1.2 `cd-stage`: remove the `ref` input, check out `github.sha`, guard the job with `github.ref == 'refs/heads/main'`, grant `id-token: write` per job, and pin the `containerapp` extension to `1.3.0b5`.
- [x] 1.3 Pin the SDK and runtime base images by digest in `src/TacticusPlanner.Api/Dockerfile`, and exclude `appsettings.Development.json` from publish.
- [x] 1.4 Git-ignore `.env*`, `*.pem`, `*.key`, `*.pfx` and `appsettings.*.local.json`.

## 2. Outbound HTTP and readiness

- [x] 2.1 Stop retrying unsafe methods in the standard resilience handler (`ServiceDefaults/Extensions.cs`).
- [x] 2.2 V1 client: `AllowAutoRedirect = false` and a 16 MB `MaxResponseContentBufferSize`. Map an oversized or failed `users/me` read to null, which becomes the existing 400.
- [x] 2.3 Output-cache `/health/ready` for 5 s under one key, ignoring query, Host and Authorization.
- [x] 2.4 Tests: `HttpClientDefaultsTests` and `HealthEndpointCachingTests`.

## 3. Column encryption

- [x] 3.1 Version-2 envelope with `table.column` AAD in `AesGcmColumnEncryptionService`. The unversioned envelope is rejected. `PlannerDbContext` converters pass each column name.
- [x] 3.2 HKDF-SHA256 hash subkey in `HmacColumnHashService`. Key decoding moves into `ColumnEncryptionOptions`, which is validated on start (skipped for OpenAPI generation).
- [x] 3.3 No migration or backfill: staging is recreated and production is provisioned after this change.
- [x] 3.4 Tests: `ColumnEncryptionTests`. Postgres fixtures are updated for the new options.

## 4. Guild authority and Tacticus user id

- [x] 4.1 `DELETE guild`:
  - require a Tacticus user id, a persisted Leader/Co-Leader role and a stored token;
  - re-verify through `GuildSyncService`, failing closed;
  - return 403 when the token answers for a different guild;
  - add `UpstreamRateLimit.Slow`.
- [x] 4.2 `POST guild/sync`: add the persisted-role pre-check and an atomic one-minute per-guild cooldown, returning 429 `guildSyncCooldown` with `Retry-After`.
- [x] 4.3 `TacticusUserIdAssignment`: GUID canonicalization, unlinking the guild member when the id changes, and mapping the unique-index violation to 409 `tacticusUserIdInUse`. It is used by `PUT account/tacticus-integration` and the V1 import, whose part codes are `invalid_tacticus_user_id` and `tacticus_user_id_conflict`.
- [x] 4.4 `DELETE account`: clear the guild-visible copies in the profile-delete transaction, and add `X-Sign-In-Account-Deleted`, exposed through CORS.
- [x] 4.5 Use `SingleOrDefault` for guild membership lookups by profile.
- [x] 4.6 Tests: `PurgeGuildEndpointTests`, `SyncMyGuildEndpointTests`, `TacticusIntegrationEndpointTests`, `V1ImportEndpointTests` and `PurgeAccountEndpointTests`. The audit reproductions are covered, inverted.

## 5. Caps and bounds

- [x] 5.1 `GoalValidation.MaxGoalsPerType` (200, non-archived). Enforce it under the profile lock on `POST goals` and `POST goals/status` (leaving Archived), returning 400 `goalLimitReached`.
- [x] 5.2 `ProjectValidation.MaxProjects` (100, non-archived). Enforce it under the profile lock on `POST projects`, returning 400 `projectLimitReached`.
- [x] 5.3 `LegendaryEventValidation.MaxTeamsPerLane` (50) on `POST …/teams`, returning 400 `legendaryEventTeamLimitReached`.
- [x] 5.4 Per-goal bounds:
  - snapshot lists: at most 250 distinct ids of at most 128 characters, each a catalog upgrade;
  - farming locations: at most 100, catalog-checked per goal type on create and edit;
  - upgrade targets: accepted only on Upgrade goals, at most 250 on create and edit;
  - `DependsOnIndex` entries distinct.
- [x] 5.5 V1 goal import:
  - at most 500 source goals (`too_many_goals`) and 1000 outcomes (`outcomes_truncated`);
  - the goal cap (`goal_limit_reached`) and the project cap (`project_limit_reached`).
- [x] 5.6 V1 LE import:
  - at most 100 events;
  - 50 teams per lane (`lane_team_limit_reached`), with member-set-key de-duplication and a precomputed sort order;
  - at most 200 issues per event (`issues_truncated`).
- [x] 5.7 Tests: `GoalCardinalityLimitTests`, `V1GoalImportEndpointTests` and `V1LegendaryEventImportEndpointTests`. Existing goal tests are adjusted for catalog-checked snapshot ids.

## 6. Contract and specs

- [x] 6.1 Verify the regenerated `artifacts/openapi/TacticusPlanner.Api.json`. It gains the new statuses and descriptions for `DELETE guild`, `POST guild/sync`, `PUT account/tacticus-integration`, `DELETE account`, `POST goals`, `POST goals/status`, `POST projects` and `POST …/teams`, and no schema change.
- [x] 6.2 Write the spec deltas for the new and modified capabilities listed in the proposal.

## 7. Gates

- [x] 7.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
- [x] 7.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore`
- [x] 7.3 `dotnet test TacticusPlanner.slnx -c Release --no-build`

## Deferred / out-of-session

- [ ] D.1 Companion `tacticus-planner-apps` change: handle the codes and statuses below (tracking issue: TBD).
  - Issue codes: `goalLimitReached`, `projectLimitReached`, `legendaryEventTeamLimitReached`, `guildSyncCooldown` (with `Retry-After`) and `tacticusUserIdInUse`.
  - The new `DELETE guild` statuses: 400, 403, 409, 429 and 503.
  - The `X-Sign-In-Account-Deleted` header.
  - The new V1 import part, outcome and issue codes listed in the proposal.
