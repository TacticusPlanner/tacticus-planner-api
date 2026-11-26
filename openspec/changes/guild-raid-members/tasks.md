## 1. Rework the branch

- [x] 1.1 Remove the capture worker, queue, lease, season planning, `GuildRaidSeasonCapture`, the two capture sync-state columns, `GET guild/raid/seasons` and their tests; delete the branch's migration; keep the normalizer, the suffix and nullable progression; verify the solution builds and the remaining tests pass
- [ ] 1.2 Merge `main` into the branch (a merge keeps the PR's review threads, unlike a rebase); verify the build and the guild sync and Guild War tests pass

## 2. Schema

- [x] 2.1 Add the three ended-season sync-state fields, `GuildRaidSeason.FinalizedAt`, `GuildPlayer`, the four `GuildMember` fields and the method enum with EF configuration; verify the solution builds
- [ ] 2.2 Generate the migration with `dotnet ef migrations add AddGuildRaidMembers --project src/TacticusPlanner.Persistence --startup-project src/TacticusPlanner.Api` and leave it unedited; verify Postgres tests cover the upgrade with existing members and attacks, uniqueness, cascade from the guild, a null progression, and that deleting a profile leaves its claim unverified

## 3. Hits

- [x] 3.1 Extract the shared normalizer from `PersistCoreAsync` with no live behavior change; verify existing raid status, projector and persistence tests pass unchanged
- [x] 3.2 Store the id suffix on new live hits and fill it on matched ones; verify with an integration test that the full id is in no raid column
- [x] 3.3 Add the own-data progression rule and entry skipping; verify unit tests for suffix, no suffix, malformed suffix, missing player, missing completion time, negative HP, and tier 6 and 7 kept as reported
- [x] 3.4 Set the ended-season pointer in `PersistCoreAsync`; verify tests for a higher season, the same season, no active season, and several seasons missed
- [x] 3.5 Add the ended-season step to the refresh operation with its hourly claim; verify tests for storing and finalizing, a changed position, a partly live season, a differing config hash, kept missing hits, an empty response with and without stored hits, the wrong season number, 404 three times, 403, timeout, two instances, joined callers, unchanged `GET guild/raid-status` body, and a failing step leaving the refresh at 200

## 4. Players and claims

- [ ] 4.1 Seed `guild_players` from roster saves and raid saves; verify tests for a former player, a suffix filled later, and concurrent seeding
- [ ] 4.2 Add `GuildClaimEvaluator` with name comparison and the stored-key rule; verify unit tests for case, spaces, NFC, duplicates on either side, Leader and Co-Leader, rejected claims, no assigned name and display name only
- [ ] 4.3 Run it on roster saves and as a player-sync step; verify integration tests for a key replaced after the sync, promotion, an existing guild, a departed only leader, a profile unlinked from another guild, and that a failing step keeps the player sync
- [ ] 4.4 Clear verification on id change and account purge, and the assigned name on a verified purge; verify tests for an id changed and changed back, and every `account-purge` scenario

## 5. Gates

- [ ] 5.1 Split the roster save from the caller check in `GuildSyncService`; verify existing register, sync and purge tests pass unchanged except where 5.2 and 5.3 change the rule
- [ ] 5.2 Gate purge before and after its sync; verify a test that enters an unclaimed Leader's id, syncs as them, and is refused with zero further upstream calls
- [ ] 5.3 Gate key replacement; verify tests for pending and verified callers, no verified leader left, two concurrent registrations, and the V1 import with an existing and a new guild

## 6. Endpoints and roster refresh

- [ ] 6.1 Add `GET guild/players`; verify tests for order, former members, every `nameMatch` value, masked ids, 403/404/409, no forbidden field in the body, and zero upstream calls
- [ ] 6.2 Add the name endpoint; verify tests for limits, clearing, duplicates, another guild's player, and automatic verification
- [ ] 6.3 Add confirm, bulk confirm and reject; verify tests for a changed claimant, own claim, Co-Leader against Leader, skipped ids, a rejected claim confirmed later, and the real owner claiming after a rejection
- [ ] 6.4 Add the caller fields to the guild, register and sync responses; verify tests for pending, verified and rejected callers, the count for a non-manager, and that assigned names are absent
- [ ] 6.5 Add `GuildRosterRefreshService` to the refresh operation; verify tests for 24 hours, one hour between attempts, the guild sync cooldown it opens, two instances, another guild's key, 401, timeout, a kicked member, and no active season
- [ ] 6.6 Add the new reads to `HandlerCommandCeilingTests`; verify the ceiling holds with 30 members and 50 former players
- [ ] 6.7 Verify the regenerated `artifacts/openapi` (built under an en-US culture) only adds operations, schemas and fields and drops nothing `main` has, and give the merge SHA to the companion apps change

## 7. Real data and gates

- [ ] 7.1 With the user's agreement and a real guild key, after season 114 ends, refresh locally once season 115 is active and record in the PR, without ids or names: hits of 114 stored live and after the read, matches, skipped entries, and the players seeded
- [ ] 7.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify all pass
- [ ] 7.3 Run `docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .` and `coderabbit review --agent --base main`; verify the build passes and valid findings are fixed
