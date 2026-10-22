# Tasks

## 1. Document drift guard

- [x] 1.1 Add a `git diff --exit-code -- artifacts/openapi` step after Build in `.github/workflows/_build-api.yml`. Verify by building locally with a hand-edited document: `git diff` shows the regenerated content, and the step command exits non-zero.

## 2. Short schema names

- [x] 2.1 Configure short schema reference ids in `Program.cs` (FastEndpoints switch if one exists, else `CreateSchemaReferenceId`; design decision 1). Verify the regenerated document has `GoalDetailResponse`, and that no schema name starts with `TacticusPlanner` or `SystemCollectionsGeneric`.
- [x] 2.2 Add the OpenAPI document test (design decision 2), covering schema-name collisions and namespace prefixes. Verify it passes, and that it fails with both types named when a duplicate-named record is temporarily added.
- [x] 2.3 Resolve any collisions the test reports by renaming the C# records. Verify the test passes and `dotnet test` stays green.
- [x] 2.4 (Added during the apps half.) Rewrite the pipeline's nullable reference shape (`type: [null, …]` + `oneOf: [$ref]`, 93 properties) as `oneOf: [{type: null}, $ref]` in a document transformer (`Http/NullableReferenceOpenApi.cs`). Verify `NullableReferencesKeepTheirNull` passes, fails when the transformer is removed, and `openapi-typescript` emits `null | Ref`.
- [x] 2.5 (Added during the apps half.) Three routes read query parameters by hand (`Query<>`/`Request.Query`), so the document had none: goals and projects `archived`, run-records `limit`/`after`. Bind them through `[QueryParam]` request DTOs. Verify `QueryParametersAreDocumented` passes and the full suite stays green.
- [x] 2.6 (Added during the apps half.) Response records with default-valued or init-only members (`GoalDetailResponse.revision/globalPriority`, `UpgradeTargetResponse` ranges, `ImportV1ProfileResponse` init members) were documented as optional although always written. Mark them `[JsonRequired]`. Verify `PlannerResponsesListEveryPropertyAsRequired` passes, and fails when one attribute is removed.
- [x] 2.7 (Added during the apps half.) Vogen value objects came out as empty schemas, and lists of them as arrays without `items` (`UnitId`, `ListOfCampaignBattleId`). Describe each as its primitive with Vogen's generated mapping (`Http/VogenOpenApiMarkers.cs`, `MapVogenTypesInVogenOpenApiMarkers`); a hand-written reflection transformer did this first and produced the identical document. Verify `EverySchemaDescribesItsShape` passes, and names both schemas when the transformer is removed.
- [x] 2.8 (Added during the apps half.) Gaps the apps migration stopped on:
  - The bare one-entry `oneOf` nullable form (`UpdateOnslaughtProgressRequest` alliances, `EditGoalRequest` sections) now keeps its null.
  - The served roster lists every property as required. First done with a transformer over the stored chunk records; replaced in review by roster response records (`RosterCharacterResponse`, `RosterMowResponse`, `RosterAbilityResponse`, `RosterEquipmentSlotResponse`) so the response no longer exposes the storage shape.
  - `MyGuildResponse.state` is the `MyGuildState` enum; onslaught `sector` is `OnslaughtSector`; `xpBookRarity` is `XpBookRarity`.
  - Verify the document tests (no exemption for player records) and the full suite pass.

## 3. Enum-typed contract fields

- [x] 3.1 Register `JsonStringEnumConverter` (no naming policy, `allowIntegerValues: false`) for request/response serialization and the OpenAPI pipeline. Verify the already-annotated guild-raid enums are unchanged in the document.
- [x] 3.2 Goals: switch fixed-value fields in request/response records to their enums, and remove the `.ToString()` mapping, the `Enum.TryParse` branches and the duplicate validator rules. Verify `GoalsEndpointTests` and `ApiErrorContractTests` pass unchanged, and the document lists `status: [Active, Paused, Completed, Archived]`.
- [x] 3.3 Add integration tests for the spec scenarios: lower-case `entityType` accepted, unknown `goalType` → 400 `validation` keyed by `goalType` with nothing created, numeric enum value rejected. Verify they pass.
- [x] 3.4 Projects, LE plans and any remaining features: switch fixed-value fields the same way. Verify their endpoint tests pass unchanged and no `Enum.TryParse` remains on request fields (`grep -rn "Enum.TryParse" src/TacticusPlanner.Api/Features`).
- [x] 3.5 Extend the document test with the enum check (an enum-backed property described as a plain string fails, naming schema and property). Verify it passes on the full document.

## 4. Integration

- [x] 4.1 Commit the regenerated `artifacts/openapi/TacticusPlanner.Api.json`, and verify a clean `dotnet build` leaves it unchanged. Then `dotnet format --verify-no-changes` and the full `dotnet test` pass.
- [x] 4.2 Run the local stack through Aspire with the current apps client and exercise goals and projects (list, create, edit, status change). Verify there are no client errors and the wire values match the pre-change strings.
  - Done 2026-10-22 against the rebuilt API in Aspire with the current apps client (desktop):
    - pause and resume through `POST goals/status`
    - Ascension goal create (`Legendary:RedThreeStars`→`Legendary:RedFourStars`)
    - goal details edit (`farmingStrategy`, acquisition kinds `Campaign`/`Onslaught`)
    - project edit with an LE target (`kind: legendaryEvent`)
    - LE team depth edit (`expectedBattleClearsSource: manual`, lanes `alpha|beta|gamma`)
  - 22 API calls, all 2xx, no console errors; wire strings identical to before. Test data left: one Incisus Ascension goal with the note "enum contract check".

## 5. Route rename

- [x] 5.1 Drop the `me/` prefix: profile resources at `/api/v1/<resource>`, `account[/display-name|/tacticus-integration|/v1-import]`, `settings`, `userjot-token`, `guild[/sync|/raid-status[/refresh]|/register]`; `PlayerDataChunkKeys.RoutePrefix` = `/api/v1/player-data`; tests and main-spec route text follow. The required-properties document test now covers every non-catalog path. Verified: the document lists exactly the new paths; 1,190 tests pass; `dotnet format` clean.

## Workflow follow-up

- Apply the companion apps change `generate-api-client-types` after this merges, pinning its `openapi/api.ref` to the merge commit.
