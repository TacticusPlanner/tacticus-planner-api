## 1. Raw data

- [x] 1.1 Add `alliesAlliance` and `alliesFactions` to every `Data/campaign-battles/campaign-battles-*.json` (22 files) per the design table; write "all Imperial/Chaos factions" out explicitly from V1 `factionData` reconciled with `Data/units/units-*.json` alliances. Keep LF, 2-space indent, trailing newline. Verify: each group file has both fields.
- [x] 1.2 Re-verify the `eventCampaign1-6` to V1 campaign pairing against each group's `faction` and file name before committing the values. Verify: pairing noted in the commit message.

## 2. Model, denormalization, validation

- [x] 2.1 Add `AlliesAlliance`/`AlliesFactions` to `GameCatalogCampaignGroup` and `GameCatalogCampaignBattleView` in `Models/Campaigns.cs`; copy them in `BuildBattleView`/`BuildCampaignBattles` (`Denormalization/CampaignDenormalizer.cs`). Verify: the served battle carries both fields.
- [x] 2.2 Add required-field (alliance, non-empty factions) and reference (known alliance, known faction ids) checks in `Validation/RequiredFieldsValidation.cs` and `Validation/ReferenceValidation.cs`. Verify: load fails with a clear error for each broken case.

## 3. Tests

- [x] 3.1 Add `TacticusPlanner.GameCatalog.Tests` coverage: every battle has non-empty allies; per-group exact values from the design table; all battles of a group share allies; validator rejects missing/unknown alliance and unknown faction id.
- [x] 3.2 Update any API test fixture that constructs `GameCatalogCampaignBattleView`.

## 4. Release

- [x] 4.1 Run the `game-catalog-data` checklist: `dotnet build TacticusPlanner.slnx`, catalog and API tests, review the `*.received.txt` diff (expect only the `campaign-battles` hash and `sourceHash`), promote the manifest snapshot with `DiffEngine_Disabled=true`, `dotnet format TacticusPlanner.slnx --verify-no-changes`. Do not bump `SchemaVersion` or `GameVersion`.
- [x] 4.2 If the battle view is part of the OpenAPI contract, verify the regenerated `artifacts/openapi` artifact.
- [x] 4.3 Run `openspec validate add-daily-raids-filters --strict` here and confirm the apps companion change of the same name is up to date; release this API change before applying the apps change.
