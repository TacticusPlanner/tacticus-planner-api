# Design

## Context

See proposal.md — Why. FastEndpoints on ASP.NET Core 10; `PlannerDbContext` pooled, global profile filters; catalog served from the in-memory `IGameCatalogProvider.Current` snapshot whose `DatasetHashes[key]` is already in every dataset envelope. The manifest endpoint already implements the ETag/304 pattern via `ETagHelper`; this design reuses it.

## Goals / Non-Goals

**Goals:**
- Goal mutations ≤ 5 DB round trips after the pre-processor (today 10–13); list goals ≤ 2.
- Catalog first visit ≥ 70 % fewer bytes; repeat dataset fetch = 304.
- Upstream call volume bounded per profile.

**Non-Goals:**
- Any contract shape change, route rename, pagination, error-shape unification, schema change — all in `rebuild-foundation-pre-launch`.
- Output caching of per-user endpoints; distributed cache.

## Decisions

1. **Dataset ETag = `ETagHelper.CreateEtag(snapshot.DatasetHashes[key])`**, `Cache-Control: public, max-age=0, must-revalidate` (datasets are anonymous and content-addressed; the manifest stays `private` because it is the sync root). Alternative: hash in the URL + `immutable` — better, but changes the manifest's `url` contract → deferred to the rebuild change.
2. **Compression via `AddResponseCompression` (Brotli, then gzip) with `EnableForHttps = true`** in `ServiceDefaults` so AppHost and container share it. BREACH is not a concern: responses carry no attacker-controlled reflected secrets (API keys are never returned; see `SecretMasker`).
3. **Rate limiting with the built-in `Microsoft.AspNetCore.RateLimiting` fixed-window policy, partitioned by profile id** (fallback: `sub` claim), applied via FastEndpoints `Options(x => x.RequireRateLimiting("upstream"))` on the six endpoints. Limits: 10 / minute per profile for `validate`/`integration`; 6 / hour for `player-sync`, `register`, `sync`, `v1-import` (the client's own sync gate is 1 h). Alternative: the existing `GuildRaidRefreshCoordinator` cooldown pattern per endpoint — rejected, bespoke state per endpoint for what the framework provides.
4. **Snapshot projection for validation:** project `Characters.FirstOrDefault(u => u.UnitId == id)` / `Mows.FirstOrDefault(...)` from the snapshot — EF Core 10 + Npgsql translate `FirstOrDefault` over an owned JSON collection to `jsonb` path operators. If translation fails for the nested `Abilities`/`EquippedItems`, fall back to projecting the two chunk columns only (still skips the other nine). `CreateCombinedGoals` resolves the roster once and passes units into `ValidateAsync` (new overload taking the resolved unit).
5. **`GoalOrderService.CompleteAsync`:** take the already-tracked profile from the caller's unit of work (no `FirstAsync` + `ReloadAsync`), project only `(Id, GlobalPriority, Status)` for the density check, and load full goal rows only when renumbering is needed. The deferrable unique constraint is unchanged.
6. **Guild sync:** `profiles.Where(p => hashes.Contains(p.TacticusUserIdHash))` — Npgsql translates `bytea = ANY(@hashes)`; the comment claiming otherwise is removed. **Raid status:** push the projector's filters (`EncounterType == Boss`, `CompletedAt >= cutoff`, latest per encounter) into the query; the projector keeps its pure in-memory shape for tests, fed by the narrower set.
7. **Player sync:** read `ChunkHashes` + metadata with a projection; write changed chunks with `ExecuteUpdateAsync` setting only those jsonb columns plus `revision`/`synced_at`/`updated_at`; insert path unchanged.
8. **Command-count assertions:** a `DbCommandInterceptor` registered only in `PlannerApiFactory` counts commands per `HttpContext`; tests assert `<= N` for `POST me/goals/combined`, `POST me/goals/{id}/status`, `PUT me/goals/order`, `GET me/goals`. This is the regression gate for every later query change.

## Risks / Trade-offs

- [EF Core JSON-collection `FirstOrDefault` translation varies by provider version] → spike first (one test against Testcontainers Postgres); the documented fallback keeps the win.
- [Rate limits too tight for V1-import retries] → `Retry-After` is honoured by the client and limits are configuration keys (`RateLimiting:*`), documented in README's settings table.
- [`ExecuteUpdateAsync` bypasses the `Revision` concurrency token] → the sync path owns the row exclusively per profile (single-flight in the client, idempotent by `configHash`); revision is still incremented in the same statement.

## Migration Plan

No migration. Deploy as a normal API release; rollback = previous image. Configuration keys for rate limits are optional with defaults.
