## 1. Guild War dataset

- [ ] 1.1 Add `globalconfig/guildWar.json` to `Data/datamine/files.txt` and copy it with `pull-datamine.cs --source`, and verify `DatamineSourceTests` passes.
- [ ] 1.2 Add the `GuildWar` served models, `GuildWarDenormalizer` and `GuildWarValidation` per the `guild-war-dataset` spec, register `guild-war` in `GameCatalogDatasets.Served`, and verify denormalizer tests for the four seasons, Season D's zones, the Armoury level-5 caps, the rarity-keyed buffs and level 0.
- [ ] 1.3 Add the `guild-war` catalog endpoint, and verify an endpoint test and the promoted manifest snapshot (one new dataset, `schemaHash` moved).

## 2. Wars and attacks

- [ ] 2.1 Add `GuildWar`, `GuildWarAttack`, their lineup and enemy records and `GuildWarValidation` limits in `TacticusPlanner.Domain/GuildWars`.
- [ ] 2.2 Add the configurations, `DbSet`s, profile query filters, the partial unique current-war index and the cascading foreign keys, then run `dotnet ef migrations add AddGuildWars`.
- [ ] 2.3 Add `GuildWarCatalogFacts` and the attack rules (`guildWarUnknownZone`, `guildWarUnknownUnit`, `guildWarUnitDeployed`, `guildWarTokensSpent`) and the derived rarity cap, and verify unit tests for each code and the Armoury slot 2 cap.
- [ ] 2.4 Add `GET`/`PUT /guild-wars/current`, `POST /guild-wars` and `GET /guild-wars`, and verify integration tests for no war, create on PUT, stale revision, start keeps the old war, unknown season and level, and paging.
- [ ] 2.5 Add attack create, update and delete, and verify integration tests for the derived tokens and deployed units, Undo, stale edit, unit deployed, tokens spent, `guildWarNotStarted` and another profile's war.
- [ ] 2.6 Carry wars and attacks in the staging transfer, and verify a round trip keeps the current war, the order and every field with new ids; verify purge removes them.
- [ ] 2.7 Verify the regenerated `artifacts/openapi` lists the endpoints with typed codes, and hand the contract to the `guild-war-offense` apps change.
