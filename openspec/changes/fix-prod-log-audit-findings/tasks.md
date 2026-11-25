## 1. Guild linking

- [x] 1.1 Sever a linked profile's link on other guilds' rosters during a guild sync
- [x] 1.2 Clear the change tracker when the guild save fails
- [x] 1.3 Integration test: a member who moved guilds is relinked on the new guild's registration

## 2. Player-data sync limit

- [x] 2.1 Add the `player-sync` bucket and `RateLimiting:PlayerDataSyncPerHour` (default 30)
- [x] 2.2 Integration tests for the separate limit; README configuration table

## 3. Telemetry

- [x] 3.1 `TelemetryNoiseProcessor` (expected 4xx as successful, fast DB spans and client-aborted outgoing spans dropped) registered before the Azure Monitor exporter
- [x] 3.2 Drop unused metric streams; HttpClient and Polly logs at Warning
- [x] 3.3 Unit tests through a real tracer pipeline

## Deferred / out-of-session

- [ ] 4.1 After the prod deploy, confirm with `tp-audit-logs` that Postgres rows in `AppDependencies` drop sharply, failed requests are 5xx only, and daily ingestion falls to about 0.35–0.45 GB
