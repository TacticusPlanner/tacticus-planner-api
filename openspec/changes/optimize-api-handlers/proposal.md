# Proposal

## Why

The audit of 2026-10-20 found a set of server-side inefficiencies that need no contract or schema change and can therefore ship ahead of the pre-launch rebuild (`rebuild-foundation-pre-launch`): the 24 game-catalog dataset endpoints (up to ~2 MB each) carry no `ETag`/`Cache-Control` and nothing is compressed; every goal-target validation loads the whole 11-column player-data snapshot row to find one unit (K times for a combined create); every goal create/status/delete reloads all in-flight goals and the profile twice; guild sync loads every linkable profile in the system; raid status loads a whole season of attacks and filters in memory; player sync loads all chunks tracked just to compare hashes. Six endpoints call upstream Tacticus/V1 per request with no throttle.

Evidence: `Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs:18-32` vs `GetGameCatalogManifestEndpoint.cs:32-42`; `Features/Goals/GoalTargetValidationService.cs:75-78` and `CreateCombinedGoalsEndpoint.cs:104`; `Features/Goals/GoalOrderService.cs:40-62`; `Features/Guilds/GuildSyncService.cs:155-177`; `Features/Guilds/GuildRaidStatusService.cs:162-180`; `Features/PlayerData/PlayerDataSyncService.cs:65,112-134`; `orchestration/TacticusPlanner.ServiceDefaults/Extensions.cs` (no compression/rate limiting).

## What Changes

- Game-catalog dataset responses carry a deterministic `ETag` (the dataset hash already in the envelope), honour `If-None-Match` with `304`, and set `Cache-Control` like the manifest does today. Response compression (Brotli/gzip) is enabled for every JSON response.
- Upstream-calling endpoints (`tacticus-api-key/validate`, `me/tacticus-integration`, `tacticus-integration/player-sync`, `guilds/register`, `guilds/me/sync`, `me/v1-import`) are rate-limited per authenticated profile; exceeding the limit returns `429` with `Retry-After`.
- Internal query efficiency, no observable change: goal-target validation projects only the targeted unit from the snapshot and a combined create resolves the roster once per request; `GoalOrderService.CompleteAsync` loads the profile once and only renumbers when the order is not already dense; guild sync filters linkable profiles by hash in SQL; raid status reads only the attacks the projector uses (boss encounters after the cutoff) in SQL; player sync reads chunk hashes only and writes changed chunks without loading the others; redundant profile existence/id queries in `ReadyGuildResolver`, `ProjectGoalPlanningService`, `GoalOrderService` are removed.
- `GET me` no longer writes `last_seen_at` on every call (at most once per hour per profile).
- Dead code removed: `GuildRaidAttackRepository` and its DI registration, the scalar `PlayerDataManifestBuilder.Build` overload. Dead **columns** (`battle_result_overrides`, `tacticus_last_updated_on`) are left for `rebuild-foundation-pre-launch` so this change needs no migration.
- Legendary-event code uses UUIDv7 ids like the rest of the API.

## Capabilities

### New Capabilities
- `game-catalog-http-caching`: conditional requests and cache headers on served catalog datasets, and compressed transport for API responses.
- `upstream-call-rate-limiting`: per-profile limits on endpoints that call the Tacticus or V1 APIs, and the `429` contract.

### Modified Capabilities
- none — the query-efficiency items change no served behavior; `guild-raid-current-status` ("Successful Guild Raid observations are retained", "Reading status never calls upstream") and `player-data-sync` ("Chunk persistence is driven by transformed content") keep their requirements.

## Impact

- `src/TacticusPlanner.Api/Features/{GameCatalog,Goals,Guilds,PlayerData,CurrentUser,LegendaryEventPlans}`, `Program.cs` (compression, rate limiter), `orchestration/TacticusPlanner.ServiceDefaults`.
- OpenAPI artifact: new `304`/`429` responses and headers; no schema changes. No migration.
- Companion `tacticus-planner-apps` change: **none required** — the catalog client already does hash-based delta sync and will simply benefit; a later apps change may add `If-None-Match` on dataset fetches.
- Prerequisite: complete `replace-inmemory-api-tests-with-postgres` first. Database-backed behavior and command measurements use `TacticusPlanner.Api.IntegrationTests/PlannerApiFactory`, with its request-correlated production PostgreSQL counter; pure/catalog tests stay in `TacticusPlanner.Api.Tests`. Endpoint ceilings and before/after measurements remain owned by this change.
