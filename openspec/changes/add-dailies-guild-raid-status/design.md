## Context

See `proposal.md` for motivation and `specs/guild-raid-current-status/spec.md` for the contract. V2 already stores a registered guild, encrypted guild API token, member links, and last synchronization timestamps. Its upstream client already models `/api/v1/guildRaid`, while the raid-boss catalog already contains ordered season configs, progression steps, loop metadata, primes, and inlined modifiers. There is no V2 endpoint or persisted raid-state model.

The companion `tacticus-planner-apps` change consumes `GET /api/v1/guilds/me/raid-status`. Raw hits and credentials stay server-side.

## Goals / Non-Goals

**Goals:**

- Persist normalized Guild Raid source facts by guild and season and produce one small, stable current-status projection from them.
- Provide an indexed, current-user-scoped read path over persisted hits without materializing the guild's complete hit collection.
- Keep automatic/manual refresh inexpensive and collapse same-guild concurrency.
- Make partial source knowledge explicit through nullable fields and stale fallback.

**Non-Goals:**

- Building historical analytics or storing derived rankings, scores, token summaries, or recommendations.
- Returning member-level data, team recommendations, token resources, or performance scores.
- Guessing a season schedule when no authored occurrence exists.

## Decisions

### Persist normalized source facts and keep the status projector pure

The endpoint will live under the existing Guild route boundary. A refresh service normalizes the upstream response into relational source facts scoped by `(guildId, seasonNumber)` and writes a complete successful observation transactionally. A pure status projector then accepts the persisted season/hits, the current raid-boss catalog snapshot, and optional active Guild Raid event occurrence and emits the declared discriminated contract. Endpoint authorization/token lookup and upstream error mapping remain outside it.

`GuildRaidSyncState`, keyed by guild, records the latest successful active/no-active outcome and observation time so either result can be reused without a process-local cache. `GuildRaidSeason` records the upstream season/config identity, last observation metadata, and guild ownership. Child hit and hit-unit records retain normalized encounter facts needed for current projection and later separately-scoped analytics. A unique guild/season key plus a deterministic hash of each normalized upstream entry makes repeated refreshes idempotent. Derived current boss, modifiers, summaries, rankings, and token counts are not persisted.

This keeps boss-order/loop/modifier cases directly testable without HTTP. Returning raw upstream entries and reproducing V1 calculations in the browser was rejected because it exposes unnecessary member data and creates two normalization paths.

### Make current-user hit reads selective by construction

Each persisted hit stores the same keyed `TacticusUserIdHash` used by `Profile` and `GuildMember`; the upstream user id is not stored as a new plaintext lookup column. The hit table has a composite index beginning with `GuildRaidSeasonId` and `TacticusUserIdHash`, followed by completion time for ordered reads. The deterministic hit identity remains a separate uniqueness constraint.

A dedicated repository query accepts the resolved guild-season id and caller's profile hash, applies both predicates in PostgreSQL, uses a no-tracking projection, and selects only the columns required by its consumer. It does not load the season's hit navigation or other members' hits. When unit details are requested, they are loaded only for the already-filtered hit ids. This prepares later current-player views without adding hit history to the status response in this change.

### Use the raid-boss season config as the only encounter sequence

The refresh normalizer strips any progression suffix from upstream unit ids. The projector resolves the current boss position using unit-set id plus difficulty/progression and, after a defeated observation, selects the current configured position using the catalog's tier/set and loop rules. It selects observations relative to the most recent boss defeat so repeated Legendary/Mythic loops cannot reuse stale HP.

Boss and prime maximum HP fall back to the exact selected catalog progression step. Modifier thresholds use the catalog's existing proportional `hpLost` semantics. No parallel hard-coded boss order will be introduced.

### Return a compact id-only contract

The endpoint returns structural ids and numeric/timestamp values. The app resolves all names, images, and localized modifier copy. The response excludes raw entries and hero details. This follows the catalog's presentation boundary while allowing the browser to render a complete status.

### Treat season timing as optional authored data

The events catalog defines Guild Raid seasons as non-recurring. At `observedAt`, the projector may use one explicit active `GuildRaidSeason` occurrence's end timestamp; otherwise `endsAt` is null. It will not derive an anchor from the first recorded hit or assume a 14-day cadence. The relevant projection is the already-loaded current catalog version/window; this change adds no forward recurrence.

### Use PostgreSQL as the durable observation cache with single-flight refresh

The service reads the latest persisted observation for the guild. An observation less than five minutes old is fresh; a normal request returns it without upstream access. Older observations trigger refresh, while `refresh=true` bypasses the fresh-age check. A bounded per-guild asynchronous gate ensures automatic and forced refresh requests within one API instance share one upstream operation.

Each successful active response transactionally upserts the guild/season observation, its normalized source facts, and the guild sync state before projection. A successful no-active response updates only the guild sync state and does not delete prior season facts. If refresh fails, the most recent persisted successful sync state and any referenced season remain available as stale data regardless of process restarts; `observedAt` makes age explicit. Single-flight remains process-local, so database uniqueness and idempotent writes handle cross-instance overlap.

### Map prerequisite and upstream failures without hiding stale data

Membership/readiness failures return conflict before upstream access. Upstream no-active responses normalize to a successful empty state. Rejected upstream credentials/data always map to bad gateway so invalid access or malformed source data is not hidden by an old snapshot. Transient network/timeouts use a previously persisted successful observation marked stale; when none exists, they map to service unavailable.

### Catalog and persistence impact

No raw catalog dataset, denormalizer, public manifest shape, or manifest snapshot changes are required. The endpoint reads the existing `raid-bosses` and current event projections. An additive EF Core migration creates guild raid sync-state, season, hit, and hit-unit storage with guild/season ownership, cascade behavior, uniqueness, and lookup indexes, including the current-user composite hit index. No backfill is required; the first status refresh populates the current guild state and season.

## Risks / Trade-offs

- [Risk] Upstream responses with no hits provide less evidence for HP/difficulty → Use the exact first configured position and catalog progression, retaining null where no trustworthy value exists.
- [Risk] A new upstream shape or unknown season config prevents detailed normalization → Fail the refresh rather than pair hit data with the wrong boss; recent cached data may be returned stale.
- [Risk] Process-local single-flight permits one refresh per deployment instance → Make refresh writes idempotent and enforce relational uniqueness so overlapping instances converge safely.
- [Risk] Persisted hit facts broaden future data-retention responsibility → Store only normalized raid facts needed for projection, never credentials or derived analytics, and defer retention/analytics policy to a separate change.
- [Risk] A convenient entity navigation could accidentally load every guild member's hits → Expose a dedicated season-and-user-hash repository projection and test its generated relational query/index usage.
- [Risk] Explicit season occurrences may be absent → Return `endsAt: null`; the companion UI communicates that countdown is unavailable.

## Migration Plan

Deploy the additive migration, API endpoint, and generated OpenAPI contract first, then apply the companion app change. Existing guilds populate their current season on first status refresh; no backfill job is required. Rollback removes the endpoint but retains captured raid facts unless a separately reviewed destructive migration removes them.
