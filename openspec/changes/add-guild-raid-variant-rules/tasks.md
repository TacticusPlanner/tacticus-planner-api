## 1. Authored Variant Data

- [ ] 1.1 Assign a stable unique id to every current exact recommendation and author five aligned slot rules plus Machine-of-War replacements from the curated source, then verify every existing boss recommendation is covered and broad Comp membership was not copied as implicit replacements.
- [ ] 1.2 Review the authored `roleId`, `essential`, and ordered replacement choices against the source/maintainer intent and verify the JSON remains id-only with no effectiveness weights, investment targets, strategy prose, or display fields.

## 2. Catalog Contract and Validation

- [ ] 2.1 Extend raw and served Guild Raid Meta records with recommendation identity, hero-slot rules, and Machine-of-War replacements, and verify compilation plus serialization tests assert the complete field/type shape.
- [ ] 2.2 Extend denormalization as a direct order-preserving projection and verify `GuildRaidMetaDenormalizerTests` cover empty and populated replacement lists without Comp inference.
- [ ] 2.3 Extend load validation for global recommendation identity, five-slot alignment, per-list uniqueness, ideal-unit exclusion, and character/Machine-of-War references, and verify `GuildRaidMetaValidationTests` cover each failure plus legal cross-rule reuse.

## 3. Manifest and Companion Coordination

- [ ] 3.1 Run the catalog/API snapshot tests, inspect the received manifest diff, and update the verified snapshot only after confirming `guild-raid-meta` and the aggregate source hash are the only changed hashes and `SchemaVersion`/`GameVersion` remain unchanged.
- [ ] 3.2 Verify the served endpoint response against the companion `tacticus-planner-apps` schema before applying the app half of `add-guild-raid-variant-rules`.

## 4. Repository Gates

- [ ] 4.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` and verify it exits successfully.
- [ ] 4.2 Run `dotnet build TacticusPlanner.slnx -c Release --no-restore` and verify it exits successfully.
- [ ] 4.3 Run `dotnet test TacticusPlanner.slnx -c Release --no-build` and verify all tests pass.
