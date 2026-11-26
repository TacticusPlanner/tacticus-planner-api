## Context

See proposal.md for motivation. Constraints from the current code:

- `GuildRaidStatusService.PersistCoreAsync` sets `ActiveSeasonId` and `ObservedAt`, so it cannot store an ended season. `ResolveProgressionIndex` throws on any hit outside today's config. `ITacticusApi.GetGuildRaidBySeasonAsync` exists and is unused.
- `GuildRaidRefreshCoordinator` joins callers within one instance only. The Dailies Guild Raids page refreshes on mount when data is over an hour old, so active guilds refresh without a new UI.
- `GET /guild` proves which role a Tacticus id has and holds no names. `GET /player` proves a name and holds no id. Nothing upstream links the two.
- `GuildSyncService.SynchronizeAsync` links profiles by id hash and needs a Leader/Co-Leader caller. It removes a profile's link from any other guild before linking it. `POST guild/sync` claims a one-minute cooldown on the guild's last sync attempt. `LinkedPlayerName` falls back to the Planner display name, so it cannot serve as proof.
- `profiles.tacticus_user_id_hash` is unique, so an impostor blocks the real owner until their id is cleared. Links are also severed by `TacticusUserIdAssignment`, the account purge, and the foreign key's set-null. A guild's departed members lose their `guild_members` row.
- Saving a personal key sets `TacticusSyncLastSucceededAt` without a snapshot; a player sync sets it and `PlayerDataSnapshot.SyncedAt` to the same instant.
- Probe with one guild's key, 2026-11-25: Tacticus serves every season from 70 to 113 by number, although it lists only 113 and 114. All hits of 112 and 113 are on today's config positions, but only 405 of 1,024 of 111 and as few as none of older seasons. No unit id carries a progression suffix. Live and by-season responses of 114, read while active, matched on all 580 hits, content hashes included. A full backfill of those seasons took 42 MB with indexes, about 1 MB per season.

## Goals / Non-Goals

**Goals:** keep each season's last hits; make a wrong identity impossible to keep by forgetting to clear something; no background work; no upstream wait beyond what a refresh already does, except twice a season or a day.

**Non-Goals:** loading older seasons (a later change, with a job library); relaxing the live refresh's strict check; proving in-game names unique; protecting against an in-game rename that copies a name, or a leaked guild key used for the very first registration. No raw dataset, denormalization or manifest change.

## Decisions

### 1. Follow-up steps inside the shared refresh operation

The coordinator's operation persists the live observation, then runs two steps, each in its own failure scope that logs and swallows: the ended-season read and the roster refresh. Callers joined on the operation wait for both. Each step is due at most once per season or day, so most refreshes do neither.

*Alternatives:* a `BackgroundService` with a queue, as first built, needs leases and lives outside the request; the reviewer wants background work to wait for a proper job library. Reading the season in a separate client call needs web work and another rate limit.

### 2. The ended season is a pointer on the sync state

`guild_raid_sync_states` gains `ended_season_number` (int, null), `ended_season_attempted_at` (timestamptz, null) and `ended_season_not_found_count` (int). `PersistCoreAsync` sets the pointer when the persisted active season is higher than the previous `ActiveSeasonId`'s season, and resets the other two. `guild_raid_seasons` gains `finalized_at` (timestamptz, null).

The step runs when an active season was persisted, the pointer is set and the last attempt is at least an hour old. It claims the attempt with one conditional `ExecuteUpdate` on `ended_season_attempted_at`; zero rows means another instance has it. The pointer, not a table of seasons, is what makes "only the season the guild saw" hold.

*Alternative:* a per-season capture table, as first built, is needed only to backfill many seasons.

The step trusts the stored key the way the live refresh does: no extra `/guild` check, because the live call a moment earlier used the same key. The response's season number is checked.

### 3. A shared hit normalizer, two progression rules

Entry validation, unit id parsing, content hash and entity building are one normalizer, shared with `PersistCoreAsync`. Live refresh passes the catalog-resolved progression and keeps throwing. The ended-season read passes the suffix value or null, and skips invalid entries. It does not fill progression from today's catalog, as a matching position is coincidence once the config changed. Rarity and maximum HP are stored, so progression can be derived later. `progression_index` becomes nullable.

`ContentHash` is unchanged, but matching compares the fields named in the spec, because `ContentHash` includes `GlobalConfigHash`. A new hit still stores `ContentHash`, so the unique index stays a backstop; a unique conflict leaves the ended season set. The season is stored in one transaction through the execution strategy; existing hits are read without their units.

### 4. Four characters on the hit

`tacticus_user_id_suffix` (`varchar(4)`, null) on `guild_raid_attacks`, the same four characters `SecretMasker` already shows guildmates. It seeds the player table and lets leaders tell former players apart.

### 5. Verification names the profile it is for

`guild_members` gains:

| Column | Type |
|---|---|
| `verified_profile_id` | uuid, null |
| `verification_method` | string, null: `Key`, `NameMatch`, `Leader` |
| `verified_at` | timestamptz, null |
| `rejected_profile_id` | uuid, null |

A claim is verified only if `profile_id = verified_profile_id`, and rejected only if `profile_id = rejected_profile_id`. The two ids are plain columns without a foreign key. A link severed by any path, including the database's set-null and the cross-guild unlink in `GuildSyncService`, is therefore unverified at once. Changing or clearing the id and the account purge also null the verification columns, so returning to the same id does not restore it.

*Alternative:* a boolean. Every severing path would have to reset it.

### 6. One evaluator

`GuildClaimEvaluator` applies, in one place and to tracked members: drop verification of changed links, the stored-key rule, then name matching. It runs inside the saves of `GuildSyncService`, the roster refresh, name changes and confirmations, and as an `IPlayerDataSyncDefaultStep` for the claimant's guild. That step runs behind a savepoint, so its failure cannot fail a player sync.

The Tacticus name is `PlayerDetails.Name` where `snapshot.SyncedAt >= integration.TacticusSyncLastSucceededAt`. Names are compared trimmed, in NFC, upper-invariant. The stored-key rule reads `Guild.ConfiguredByProfileId`, so existing guilds need no data migration: they are verified at their next sync or daily refresh.

### 7. A player table, seeded at runtime

`guild_players`: `id` (uuid v7), `guild_id` (cascade), `tacticus_user_id_hash` (bytea 32), `tacticus_user_id_suffix` (varchar 4, null), `assigned_name` (varchar 50, null), revision and timestamps; unique on guild and hash.

An idempotent insert (`ON CONFLICT DO NOTHING`, then fill a null suffix) adds missing players from the roster in every roster save, and from distinct hit players after every raid save, live or ended. Hits stored before this change have no suffix until stored again, so their players show without a masked id.

*Alternatives:* names on `guild_members` disappear with the member; names on hits repeat thousands of times. The random id is what later slices expose instead of a hash.

### 8. The claim token

`profileClaimId` is a keyed hash, from the existing hash service, of member id and profile id, hex encoded and truncated to 32 characters. The leader echoes it, so a decision cannot land on another account. It is not a profile id and needs no column.

### 9. Reject clears the id through the existing path

Reject sets `rejected_profile_id`, then calls `TacticusUserIdAssignment.AssignAsync` with null for the claimant if their hash still equals the member's. It is the only place a leader changes another profile, and it touches only the id and what follows from it. The member's revision guards concurrent decisions (409 `concurrency`).

*Alternative:* only blocking the claim would leave the real owner locked out by the unique index.

### 10. Registration gate inside the sync

The gate needs the fresh roster, so it sits in `SynchronizeAsync` after the caller's role check and before any change: if any existing member that is Leader or Co-Leader upstream is verified and the caller is not, return a new `VerificationRequired` result, but only when a key would be persisted. `POST guild/sync` is not gated, so today's pages keep working. The V1 import maps the result to its existing guild part failure.

### 11. Daily roster refresh in the refresh operation

`GuildSyncService` is split so the roster save no longer needs a caller. `GuildRosterRefreshService.RunIfDueAsync` claims the guild with one `ExecuteUpdate` on `LastSyncAttemptedAt` (last success at least 24 hours ago, last attempt at least one hour ago), so it also opens the guild sync cooldown. It then checks the guild id hash and saves. It runs after the ended-season step, whether or not a season is active.

### 12. Contract with the web client

The shared surface is the endpoints, fields and issue codes in the `guild-member-verification` spec. The companion apps change `guild-raid-members` consumes them and refreshes `api.json`, `api.ref` and `schema.d.ts`. All response changes are additive. Until it ships, the deployed client shows the server message on a 403 from register, and a role message on one from delete.

## Risks / Trade-offs

- [A season nobody refreshes after it ended loses its last hits] → accepted; any member opening Dailies Guild Raids in the next season reads it, and a season Tacticus still serves can be loaded by the later change.
- [A refresh that reads the ended season or the roster takes one or two upstream calls longer] → at most once per season and once a day; the client already shows a refreshing state.
- [Past responses may differ from live ones in more than the config hash] → 580 of 580 matched while active; checking a live-stored season after it ended is task 6.1.
- [Deploy overlap: the old build reads a null `progression_index`] → it loads attacks only for the active season, where the value is always set.
- [A rename in game copies an assigned name] → works only for Officer and Member, needs the full id, and a leader can reject.
- [Whoever registers first with a shared key becomes verified] → accepted. A verified Leader can reject a Co-Leader, not the reverse. A false *Leader* or a false first Co-Leader who never confirms the real one needs operator help. The registrar of an existing guild is verified by the same rule, with the same remedy.
- [Guild with one verified but inactive leader] → nobody else can replace an expired key until that leader confirms someone, leaves, or is demoted in game. Leaders are told to confirm a second one.
- [A leader clears a real member's id out of malice] → the member enters it again; only a leader can then confirm it.
- [An impostor re-enters a rejected id before the owner] → shown as rejected, gets nothing, can be rejected again.
- [Names of people without a Planner account] → visible to verified leaders only; the companion change updates the privacy notice before release.

## Migration Plan

One EF Core migration, generated after `main`'s latest: the table, four nullable member columns, three sync-state columns, `finalized_at`, the suffix, and `progression_index` dropped to nullable. No data migration; players, suffixes and first leaders appear at runtime. During deploy overlap the old build ignores the new columns, and a link it changes fails the id comparison, so it is unverified. Rollback: redeploy the previous build; the purge and registration gates disappear with it. Do not revert the migration itself once ended seasons are stored, because rows with a null progression would block it.

## Open Questions

- Are in-game names unique? Answering it would only change how strong a name match is; the duplicate rule holds either way.
