# Tasks

Companion apps change: `add-quest-event-tracker` in `tacticus-planner-apps`. This API half applies first.

## 1. Raw data

- [ ] 1.1 Copy the Cyrus, Nubari and Kîmm records of V1 `hero-quests.json` to `Data/quests/quest-events.json` unchanged, and port the wiki QuestCrates table into `Data/quests/quest-crates.json`
- [ ] 1.2 Register the raw and served keys in `GameCatalogDatasets`

## 2. Models and denormalization

- [ ] 2.1 Add `Models/QuestEvents.cs` (raw and served records per the spec shapes)
- [ ] 2.2 Implement `QuestEventsDenormalizer`: tier from index, tier position, deploy slots by the rule, enemy grouping with the null-index default, typed objectives, loot, ordering; verify with unit tests for Cyrus battles 1, 3 and 17, the Rare tier slots and Nubari battle 9's power-up
- [ ] 2.3 Serve `quest-crates` and verify crate 17

## 3. Validation

- [ ] 3.1 Add `QuestEventsValidation` covering every rule in the spec, and verify with one failing test per rule plus the shipped-data pass

## 4. Endpoints and snapshot

- [ ] 4.1 Add the two `ServedDatasetEndpoint` endpoints and regenerate the OpenAPI artifact
- [ ] 4.2 Update the manifest snapshot and confirm `SchemaVersion` is unchanged

## 5. Docs and gates

- [ ] 5.1 Describe both datasets in `.agents/skills/game-catalog-data/SKILL.md` and its `.claude` mirror
- [ ] 5.2 Run `dotnet build`, `dotnet test` and the format check, all clean

## Workflow follow-up

- Archive this change in the PR before merge.
