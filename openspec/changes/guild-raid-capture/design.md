## Context

See proposal.md for motivation. Constraints from the current code:

- `GuildRaidStatusService.PersistCoreAsync` sets `ActiveSeasonId` and `ObservedAt`, so it cannot store a past season. `ResolveProgressionIndex` throws on any hit outside today's config.
- Compared with V1's committed copies, today's five configs hold a different unit at 30 to 57 of 75 positions than at some point during the last 11 seasons. The number of real seasons affected is not measured.
- `GuildRaidRefreshCoordinator` joins callers within one instance only. There is no scheduler. The Dailies Guild Raids page refreshes on mount when data is over an hour old, so active guilds trigger refresh without a new UI.
- `ITacticusApi.GetGuildRaidBySeasonAsync` exists and is unused.
- Probed with one guild's key on 2026-11-25: the season list names 113 and 114, yet 70 to 113 all return hits (295 to 1,129 each, 41,863 in total). 64 to 69 answer 200 with no entries. Season 70 starts on 2025-03-17, mid-season, so it looks like the start of Tacticus' recording; other guilds were not checked.
- In that probe all hits of 112 and 113 are on today's config positions, but only 405 of 1,024 of 111 and as few as none of older seasons. No unit id carries a progression suffix.
- `GuildRaidStatusProjector` does not read `GuildRaidAttack.ProgressionIndex`.

## Goals / Non-Goals

**Goals:** store ended seasons without touching live status; survive restarts and several instances; keep upstream use small.

**Non-Goals:** relaxing the live refresh's strict check (final capture later recovers what it rejects); versioned season configs; loop derivation. No raw dataset, denormalization or public manifest changes.

## Decisions

### 1. A background worker started by refresh

`GuildRaidStatusService` enqueues the guild id after persisting an active season. A `BackgroundService` reads a bounded `Channel<GuildId>` (capacity 100, dropping writes when full), opens its own DI scope and calls `GuildRaidSeasonCaptureService.RunAsync`. It works on one guild and one season at a time, with a one-second pause between upstream requests. Exceptions are logged and never stop the worker.

The queue may be lost on restart or scale to zero; the next refresh enqueues again.

*Alternatives:* capturing inside the refresh would add up to 44 slow requests to a user's wait. A client-called endpoint needs web work and another rate limit. A scheduler is new infrastructure and would spend keys of inactive guilds.

### 2. Capture state is separate from season data

New `guild_raid_season_captures`, key `(guild_id, season_number)`, cascade from `guilds`:

| Column | Type |
|---|---|
| `status` | string: `Pending`, `Final`, `Unavailable` |
| `not_found_attempts` | int |
| `skipped_entry_count` | int |
| `last_attempted_at`, `finalized_at` | timestamptz, null |
| `created_at`, `updated_at` | timestamptz |

A season can thus be pending before any `guild_raid_seasons` row, which needs a config id. `live` is never stored; it is derived from `ActiveSeasonId`.

`guild_raid_sync_states` gains `seasons_planned_for` (int, null) and `capture_not_before` (timestamptz, null).

### 3. A database lease limits runs

The run claims the guild with one `ExecuteUpdate` that sets `capture_not_before = now + 10 minutes` where it is null or in the past; zero rows means stop. Before each season the lease is renewed by a conditional update on the value it last wrote; zero rows means another run took over, so it stops. At the end, also conditionally, it becomes now plus one hour if seasons remain pending or the run failed, otherwise null.

Before claiming, a read decides whether anything is due: `seasons_planned_for` differs from the active season, or a pending row exists below it. Idle refreshes therefore make no upstream call.

Live refresh tracks and saves the same sync-state row, but EF updates only the columns it changed, so it does not overwrite the lease.

### 4. Guild check on every run with work

Raid responses carry no guild id. Each claimed run calls `/guild` once and compares the id's keyed hash with `TacticusGuildIdHash`. Members and roles are not updated. A mismatch ends the run. Seasons are planned only when `seasons_planned_for` differs from the active season.

After that, a season whose own data cannot be read is skipped for the run, so it cannot hold up newer seasons. Failures that would hit every season (key, timeout, server error) end it.

Planning adds a `Pending` row, in one transaction with `seasons_planned_for`, for every season from 70 to the one before the active season and every older stored season that has none. The list in the `/guild` response is ignored. Requests go oldest first, because those seasons are the likeliest to disappear.

*Alternatives:* trusting the list loses 43 seasons still served. Walking down until seasons come back empty would also find other guilds' older seasons, if there are any, but a guild may have skipped a season; the constant can be lowered later, and the next season change then plans the rest.

The active season is re-read after `/guild`, and only lower numbers are requested. That is the proof that a season ended.

### 5. A shared hit normalizer, two progression rules

Extract entry validation, unit id parsing, content hash and entity building from `PersistCoreAsync` into one normalizer. Live refresh passes the catalog-resolved progression and keeps throwing. Capture passes the suffix value or null, and skips invalid entries. It does not fill progression from today's catalog: for seasons with a changed config a matching position is coincidence. Rarity and maximum HP are stored, so it can be derived later. `progression_index` becomes nullable.

`ContentHash` is unchanged. Capture compares the fields named in the spec instead, because `ContentHash` includes `GlobalConfigHash`, which may differ in a later response. A new hit still stores `ContentHash`, so the unique index stays a backstop.

Each season is stored in one transaction through the execution strategy: season row, hits, capture row. The change tracker is cleared between seasons. A unique conflict leaves the season pending. Existing hits are read without their units.

### 6. Four characters on the hit

`tacticus_user_id_suffix` (`varchar(4)`, null) on `guild_raid_attacks`, the same four characters `SecretMasker` already shows guildmates. A per-guild player table would be smaller, but the next slice introduces that table with names; a suffix on the hit can seed it and keeps this change independent.

### 7. Contract with the web client

`GET /api/v1/guild/raid/seasons` is the only shared surface, with the shape in the spec. The companion apps change `guild-raid-capture` refreshes `api.json`, `api.ref` and `schema.d.ts`. The endpoint reads the membership, active season, stored seasons (with an `EXISTS` for hits) and captures. It does not use `ReadyGuildResolver`, so a guild without a stored key can still see its statuses.

## Risks / Trade-offs

- [Guilds nobody refreshes lose seasons] → accepted, as a scheduler was ruled out; any member opening Dailies Guild Raids triggers capture.
- [Scale to zero interrupts runs] → per-season commits, lease expiry, resume.
- [Past responses may differ from live ones in more than the config hash] → checked on a real guild before release (task 5.1). A difference in a matched field would duplicate hits; change the match rule in the spec first.
- [Deploy overlap: the old build reads a null `progression_index`] → it loads attacks only for the active season, where the value is always set.
- [Database growth: measured at 42 MB for 42,445 hits with indexes, so about 1 MB per season and 26 MB a year after the backfill] → 100 guilds take about 4.2 GB of the 32 GB disk, 500 about 21 GB. Accepted to keep all data; watch disk use after release.
- [CPU credits while many guilds backfill after release] → one worker per instance, one season at a time.
- [Seasons of other guilds before 70] → not captured; nothing suggests they exist.
- [Hits kept without end date] → decided; no read exposes them here. The privacy notice changes with the slice that lets guildmates see hits.

## Migration Plan

One EF Core migration: new table, three nullable columns, `progression_index` dropped to nullable. No data migration; existing attacks get a suffix only when matched again. Rollback: redeploy the previous build, which ignores the new table and columns and never loads past seasons. Do not revert the migration itself, because rows with a null progression would block it.
