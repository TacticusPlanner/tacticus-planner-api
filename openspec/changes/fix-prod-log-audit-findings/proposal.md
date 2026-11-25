## Why

A prod log audit (18–25 Nov 2026) found two user-facing faults and a lot of telemetry noise:

- Guild registration failed with 409 when one of the guild's members had moved from another registered guild that had not synced since: that guild's roster still linked the member's profile, which breaks the one-guild-per-profile index (`ix_guild_members_profile_id`). In the V1 import the same failure became a 500 (four on 21 Nov): the rejected rows stayed tracked and the import's next save replayed them.
- About 400 `POST player-data/sync` requests a week were turned away with 429 across about 128 profiles. Auto-sync runs hourly, so these are players pressing "Sync now" more than 10 times an hour, the limit it shares with V1 import and guild calls.
- Expected 4xx responses showed as failed requests, client-cancelled requests left exception records, upstream HttpClient and Polly info logs flooded the container console, and fast successful Postgres spans were about half of all Log Analytics ingestion (prod's biggest cost line).

No companion apps change: the client already honours `Retry-After`, and nothing else in the contract changes.

## What Changes

- A guild sync severs a linked profile's link on any other guild's roster before linking it to the synchronized guild; the fresh upstream roster proves where the player is now.
- A failed guild save clears the change tracker, so callers that keep saving (the V1 import) do not replay it.
- `POST player-data/sync` gets its own per-profile bucket, `RateLimiting:PlayerDataSyncPerHour`, default 30 an hour. The other `upstream-slow` endpoints keep 10.
- Telemetry: expected client errors (400, 401, 404, 409, 429, 499) are exported as successful requests; Postgres spans are exported only when they fail or take at least 100 ms; failed outgoing spans of a request the client already closed are not exported; three unused metric streams are dropped; `System.Net.Http.HttpClient` and `Polly` log at Warning.

## Capabilities

### Modified Capabilities

- `upstream-call-rate-limiting`: player-data sync has its own limit, default 30 an hour.
- `guild-authority`: a sync moves a profile's link from a stale roster to the synchronized guild.

## Impact

- `src/TacticusPlanner.Api/Features/Guilds/GuildSyncService.cs`, `Features/UpstreamRateLimiting`, `Features/PlayerData/PlayerSyncEndpoint.cs`, `appsettings.json`, README configuration table.
- `orchestration/TacticusPlanner.ServiceDefaults`: `TelemetryNoiseProcessor` and metric views.
- No database, migration, contract or infra changes. The new setting is optional.
