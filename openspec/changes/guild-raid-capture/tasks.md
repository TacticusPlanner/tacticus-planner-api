## 1. Schema

- [x] 1.1 Add `GuildRaidSeasonCapture` and its status enum, the two `GuildRaidSyncState` fields, `GuildRaidAttack.TacticusUserIdSuffix` and nullable `ProgressionIndex`, with EF configuration; verify the solution builds
- [x] 1.2 Generate the migration with `dotnet ef migrations add AddGuildRaidSeasonCapture --project src/TacticusPlanner.Persistence --startup-project src/TacticusPlanner.Api`; verify `GuildRaidPersistencePostgresTests` cover the upgrade with existing attacks, cascade delete from the guild, and a null progression

## 2. Hit normalizer

- [x] 2.1 Extract the shared normalizer from `PersistCoreAsync` with no live behavior change; verify existing raid status, projector and persistence tests pass unchanged
- [x] 2.2 Store the id suffix on new live hits and fill it on matched ones; verify with an integration test that the full id is in no raid column
- [x] 2.3 Add the capture progression rule and entry skipping; verify unit tests for suffix, no suffix, malformed suffix, missing player, missing completion time, negative HP, and tier 6 and 7 kept as reported

## 3. Capture

- [x] 3.1 Implement the due check, lease claim, renewal and release; verify Postgres tests with two concurrent claims, an expired lease, a taken-over lease, the one-hour gap, and a live refresh saving during a lease
- [x] 3.2 Implement the guild check and season planning; verify tests for a different guild, seasons Tacticus does not list, a stored older season, a new active season, unchanged members and roles, and no `/guild` call when nothing is due
- [x] 3.3 Implement per-season storing; verify tests for an unknown config, a changed position, a partly live season, a differing config hash, kept missing hits, an empty response with and without stored hits, and the wrong season number
- [x] 3.4 Implement failure handling; verify tests for 400, 401, 403, 404 three times, timeout, 5xx, and an unreadable season that newer ones are captured past, each asserting statuses and the number of upstream calls
- [x] 3.5 Add the queue and background worker and enqueue after a persisted active season; verify tests that refresh does not wait, a capture exception leaves refresh at 200 and the worker running, and cooldown, failed, no-active and `GET` paths enqueue nothing
- [x] 3.6 Extend `FakeTacticusApi` with per-season responses and call counts; verify a ten-season backfill resumes after an interruption with seven remaining season calls and leaves the `GET guild/raid-status` body identical

## 4. Season status endpoint

- [x] 4.1 Add `GET guild/raid/seasons`; verify tests for all four statuses, order, empty list, plain member, no stored key, 404, 409 `guildRaidNotReady`, and zero upstream calls
- [x] 4.2 Add it to `HandlerCommandCeilingTests`; verify the ceiling passes with eleven seasons
- [ ] 4.3 Verify the regenerated `artifacts/openapi` (built under an en-US culture) adds only the new operation and schemas, and give the merge SHA to the companion apps change

## 5. Real data and gates

- [x] 5.1 With the user's agreement and a real guild key, run a local backfill and record in the PR, without ids or names: seasons captured, skipped entries, seasons off today's config, whether hits stored live match their past-season copies, and database size. Compare tokens, bombs, total damage and loop token counts of one season with V1, and note whether upstream `tier` identifies loops. If the key owner is an Officer, use a local, uncommitted build that also accepts Officer in `GuildSyncService`, and say so in the PR; the branch keeps Leader/Co-Leader
  - Result (2026-11-25, Officer build, capture triggered at API start): 44 of 44 ended seasons (70 to 113) final in about two minutes, 41,863 hits, 0 skipped, every season's count equal to a separate read of Tacticus. Hits table 42 MB with indexes, database 51 MB.
  - On today's config: all of 112 and 113, 405 of 1,024 of 111, fewer in most older ones. Progression is null on every past hit, as no unit id has a suffix.
  - Live and by-season responses of 114, read while it was active: 580 of 580 hits match, content hashes included. A live-stored season read again after it ended is still to be observed.
  - Upstream `tier` keeps counting past 5 and identifies loops. Season 113: 784 tokens, 286 bombs, 482,043,137 damage; tokens 328 in the first pass, 268 and 188 in the loops.
  - V1 comparison, one player only: in 113, 28 tokens, 12 bombs, 2 prime tokens, 0 boss kills, 27.0M battle damage, and every per-target average (6) and maximum (7) V1 shows are equal. In 114, all 21 hits V1 listed under its filters are stored. Guild totals and loop tokens were not compared with V1.
- [x] 5.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all pass
- [ ] 5.3 Run `docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .` and `coderabbit review --agent --base main`; verify the build passes and valid findings are fixed
