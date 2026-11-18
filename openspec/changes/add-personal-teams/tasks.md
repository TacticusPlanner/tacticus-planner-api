## 1. Domain and persistence

- [x] 1.1 Add `Team`, `TeamId` and `TeamValidation` limits in `TacticusPlanner.Domain/Teams`, following `Project`, and verify the solution builds.
- [x] 1.2 Add `TeamRules.Evaluate` returning the rule codes from the `personal-teams` spec, and verify unit tests cover every code plus each spec scenario, including the Rukkatrukk, two-Incursion and core-character-as-flex cases.
- [x] 1.3 Add `TeamConfiguration`, the `Teams` `DbSet`, the profile `HasQueryFilter`, the unique `(profile_id, name_key)` index, the partial unique `(profile_id, import_key)` index and the cascading profile foreign key.
- [x] 1.4 Run `dotnet ef migrations add AddTeams`, and verify a persistence integration test covers both unique indexes and that purging a profile removes its teams.
- [x] 1.5 Add `TeamOptions.Limit` (default 20) bound from configuration, and verify creates and the import both read it.

## 2. Teams endpoints

- [x] 2.1 Add `TeamCatalogFacts` built from `GameCatalogSnapshot`, and verify a test on the shipped catalog: `elite1` is not a campaign, `campaign1` has its 3 core characters, and `orksRukkatrukk` allows 6 factions.
- [x] 2.2 Add `GET /api/v1/teams` with derived `issues` and contiguous `position`, and verify integration tests for ordering, profile isolation and an `unknownUnit` issue.
- [x] 2.3 Add `POST /api/v1/teams` with name, notes, limit and rule refusals under the profile lock, and verify integration tests for `teamNameTaken`, `teamLimitReached` (including concurrent creates) and a refused rule.
- [x] 2.4 Add `PUT /api/v1/teams/{teamId}` with revision checks, and verify integration tests for `teamRevisionStale` with the current team, a stable id across a rename, and a rename refused on a team with issues.
- [x] 2.5 Add `DELETE /api/v1/teams/{teamId}` and `PUT /api/v1/teams/order`, and verify integration tests for position gaps, `teamOrderStale` and that a reorder changes no other field.
- [x] 2.6 Type `issues` and refused-save codes as one `TeamRuleCode` enum in the OpenAPI document, and verify the generated contract lists exactly the rules-table codes.
- [x] 2.7 Check each new handler against `HandlerCommandCeilingTests` and add the teams handlers to it where the suite expects them.

## 3. V1 import

- [x] 3.1 Move `UnitResolver` and its aliases to a shared type in `Features/V1Import`, and verify the existing Legendary Event import tests pass unchanged.
- [x] 3.2 Add `Teams2` to `V1UserData` and `TacticusV1Client.ReadTeams`, and verify parsing tests in `V1ImportParsingTests` for absent, empty, non-array and partly unreadable data.
- [x] 3.3 Add `Teams` (default `false`) to `ImportV1Selection` beside the merged `RosterSnapshots`, plus `Teams` and `TeamOutcomes` on the response, and verify omitting the flag leaves the other parts unchanged.
- [x] 3.4 Add `V1TeamImportService` with mode, campaign, flex, name and notes mapping and outcomes, and verify tests for the 14 campaign keys, the flex scenario, name and notes shortening, and rule issues listed on outcomes.
- [x] 3.5 Store the import key and add re-run and limit handling under the profile lock, and verify integration tests for a full re-run (`already_imported`), a re-run after a rename, a re-run after a delete, a collision with a team made in V2 (`duplicate_name`), keys colliding inside one import, and 18 existing teams plus 4 imports.

## 4. Staging transfer

- [x] 4.1 Add `Teams` (with `ImportKey`) to `StagingTransferBundle`, the exporter, the importer and the import counts, and verify `StagingTransferEndpointTests` covers a 3-team round trip with new ids, an unchanged order and kept import keys.

## 5. Contract and gates

- [x] 5.1 Build, then verify the regenerated `artifacts/openapi/TacticusPlanner.Api.json` contains the teams endpoints and import fields, and that the working-tree change already present in that file is not swept in unintentionally.
- [x] 5.2 Confirm the contract with the companion `add-personal-teams` change in `tacticus-planner-apps`.
- [x] 5.3 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` and `dotnet test TacticusPlanner.slnx -c Release --no-build`, and verify all pass.
