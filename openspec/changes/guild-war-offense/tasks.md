## 1. Guild War dataset

- [x] 1.1 Add `globalconfig/guildWar.json` to `Data/datamine/files.txt` and copy it with `pull-datamine.cs --source`, and verify `DatamineSourceTests` passes.
- [x] 1.2 Add the `GuildWar` served models, `GuildWarDenormalizer` (which validates the season data) per the `guild-war-dataset` spec, register `guild-war` in `GameCatalogDatasets.Served`, and verify denormalizer tests for the four seasons, Season D's zones, the Armoury level-5 caps, the rarity-keyed buffs and level 0.
- [x] 1.3 Add the `guild-war` catalog endpoint, and verify an endpoint test and the promoted manifest snapshot (one new dataset, `schemaHash` moved).

## 2. Wars and attacks

- [x] 2.1 Add `GuildWar`, `GuildWarAttack`, their lineup and enemy records and `GuildWarValidation` limits in `TacticusPlanner.Domain/GuildWars`.
- [x] 2.2 Add the configurations, `DbSet`s, profile query filters, the partial unique current-war index and the cascading foreign keys, then run `dotnet ef migrations add AddGuildWars`.
- [x] 2.3 Add `GuildWarCatalogFacts` and the attack rules (`guildWarUnknownZone`, `guildWarUnknownUnit`, `guildWarUnitDeployed`, `guildWarTokensSpent`) and the derived rarity cap, and verify unit tests for each code and the Armoury slot 2 cap.
- [x] 2.4 Add `GET`/`PUT /guild-wars/current`, `POST /guild-wars` and `GET /guild-wars`, and verify integration tests for no war, create on PUT, stale revision, start keeps the old war, unknown season and level, and paging.
- [x] 2.5 Add attack create, update and delete, and verify integration tests for the derived tokens and deployed units, Undo, stale edit, unit deployed, tokens spent, `guildWarNotStarted` and another profile's war.
- [x] 2.6 Carry wars and attacks in the staging transfer, and verify a round trip keeps the current war, the order and every field with new ids; verify purge removes them.
- [x] 2.7 Verify the regenerated `artifacts/openapi` lists the endpoints and names each `issueCode` in their responses, and hand the contract to the `guild-war-offense` apps change.

## 3. Review follow-ups

- [x] 3.1 Let an update that keeps its zone and slot skip the zone check, drop the fallback token cap, lock a war's season once it has attacks (`guildWarHasAttacks`), make a war start idempotent with `currentWarId`, bound `attackedAt`, require a rank for characters only, and report a null buff variable as a load failure; verify unit and integration tests for each.
- [x] 3.2 Replace `unitsLost` with `lostUnitIds` (lineup units, only with a result) and add `notes` (200 characters), regenerate `AddGuildWars`, and carry both in the staging transfer.
- [x] 3.3 Add `seasonConfigId` to the `guild-war-season` calendar occurrences (season 28 and 29 run `guildWarSeasonC`), validate it against the served seasons, and verify a validation test.
