## Context

See `proposal.md` for motivation and `specs/guild-raid-current-status/spec.md` for the contract. V2 already stores a registered guild, encrypted guild API token, member links, and last synchronization timestamps. Its upstream client already models `/api/v1/guildRaid`, while the raid-boss catalog already contains ordered season configs, progression steps, loop metadata, primes, and inlined modifiers. There is no V2 endpoint or persisted raid-state model.

The companion `tacticus-planner-apps` change consumes `GET /api/v1/guilds/me/raid-status`. Raw hits and credentials stay server-side.

## Goals / Non-Goals

**Goals:**

- Produce one small, stable current-status projection from upstream observations and catalog data.
- Keep automatic/manual refresh inexpensive and collapse same-guild concurrency.
- Make partial source knowledge explicit through nullable fields and stale fallback.

**Non-Goals:**

- Persisting Guild Raid seasons/hits or building historical analytics.
- Returning member-level data, team recommendations, token resources, or performance scores.
- Guessing a season schedule when no authored occurrence exists.

## Decisions

### Add a dedicated Guild Raid status feature and pure projector

The endpoint will live under the existing Guild route boundary but delegate normalization to a pure status projector. The projector accepts the upstream response, the current raid-boss catalog snapshot, observation time, and optional active Guild Raid event occurrence, then emits the declared discriminated contract. Endpoint authorization/token lookup and upstream error mapping remain outside it.

This keeps boss-order/loop/modifier cases directly testable without HTTP or persistence. Returning raw upstream entries and reproducing V1 calculations in the browser was rejected because it exposes unnecessary member data and creates two normalization paths.

### Use the raid-boss season config as the only encounter sequence

The projector strips any progression suffix from upstream unit ids, resolves the matching boss position using unit-set id plus difficulty/progression, and advances with the catalog's tier/set and loop rules. It selects observations relative to the most recent boss defeat so repeated Legendary/Mythic loops cannot reuse stale HP.

Boss and prime maximum HP fall back to the exact selected catalog progression step. Modifier thresholds use the catalog's existing proportional `hpLost` semantics. No parallel hard-coded boss order will be introduced.

### Return a compact id-only contract

The endpoint returns structural ids and numeric/timestamp values. The app resolves all names, images, and localized modifier copy. The response excludes raw entries and hero details. This follows the catalog's presentation boundary while allowing the browser to render a complete status.

### Treat season timing as optional authored data

The events catalog defines Guild Raid seasons as non-recurring. At `observedAt`, the projector may use one explicit active `GuildRaidSeason` occurrence's end timestamp; otherwise `endsAt` is null. It will not derive an anchor from the first recorded hit or assume a 14-day cadence. The relevant projection is the already-loaded current catalog version/window; this change adds no forward recurrence.

### Use bounded in-memory stale-while-error caching with single-flight refresh

A singleton status service keeps the last successful active/no-active observation by guild id. Five minutes is fresh; successful data remains eligible for stale fallback through 30 minutes. A per-guild asynchronous gate ensures both stale automatic requests and `refresh=true` share one upstream call. Entries are bounded by expiration/size policy so the lock/cache key set cannot grow indefinitely.

Process-local caching was chosen over a database table because status is ephemeral and no historical fact retention is required. In a multi-instance deployment, single-flight is per instance; source rate limiting remains bounded by each instance. A distributed cache can replace this behind the service later without changing the endpoint.

### Map prerequisite and upstream failures without hiding stale data

Membership/readiness failures return conflict before upstream access. Upstream no-active responses normalize to a successful empty state. Rejected upstream credentials/data map to bad gateway; transient network/timeouts map to service unavailable. A recent successful cached projection wins over transient refresh errors and is marked stale.

### Catalog and persistence impact

No raw catalog dataset, denormalizer, public manifest shape, or manifest snapshot changes for this endpoint. It reads the existing `raid-bosses` and current event projections. No EF Core migration or backfill is required.

## Risks / Trade-offs

- [Risk] Upstream responses with no hits provide less evidence for HP/difficulty → Use the exact first configured position and catalog progression, retaining null where no trustworthy value exists.
- [Risk] A new upstream shape or unknown season config prevents detailed normalization → Fail the refresh rather than pair hit data with the wrong boss; recent cached data may be returned stale.
- [Risk] Process-local single-flight permits one refresh per deployment instance → Keep the cache abstraction replaceable and monitor upstream pressure before adding distributed coordination.
- [Risk] Explicit season occurrences may be absent → Return `endsAt: null`; the companion UI communicates that countdown is unavailable.

## Migration Plan

Deploy the API endpoint and generated OpenAPI contract first, then apply the companion app change. The endpoint is additive and requires no data migration. Rollback removes the endpoint/cache service; existing guild and catalog data are untouched.
