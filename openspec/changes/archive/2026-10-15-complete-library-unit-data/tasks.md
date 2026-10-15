## 1. MoW mythic ability

- [x] 1.1 Add `GameCatalogMowMythicAbility(string Name)` and an optional `MythicAbility` to `GameCatalogMow` (`Models/Mows.cs`); verify an existing MoW denormalizer/loader test still passes with the field absent.
- [x] 1.2 Add `"mythicAbility": { "name": "…" }` to each of the 11 raw `mows[]` records in `Data/units/units-*.json` from V1 `mow/data/new-mows-data2.json` `mythicAbilities[0]` matched by `id` (LF, 2-space, trailing newline); verify with a GameCatalog test that all 11 served MoWs carry a distinct non-empty name and `ultraDreadnought` has `WisdomOfTheAncients`.
- [x] 1.3 Add load validation (empty name, or equal to the MoW's primary/secondary ability name) and a `GameCatalog.Tests` case per failure; verify the tests fail before the validation and pass after.

## 2. Mythic quests dataset

- [x] 2.1 Port V1 `mythic_quests/data/mythic-quests.json` `quests[]` to `Data/mythic-quests.json` with a one-off script (not committed): `characterId`, `quests[].number`, `quests[].rewards` (strings unchanged), `quests[].tasks[]` `locaKey`/`target`/`taskParameters`; drop `characterName`, quest `name`, task `name` and `terms[]` (design D2); verify 117 records × 5 quests and that `git diff --check` is clean.
- [x] 2.2 Add the raw model, register raw key `mythic-quests` and served key `mythic-quests` in `GameCatalogDatasets` (`Served` list), and add `BuildMythicQuests` producing the served shape in `specs/mythic-quests-dataset/spec.md` (rename to `unitId`/`parameters`, ordering, closed reward parser per design D3); verify with `MythicQuestDenormalizerTests` covering the Abaddon quest 1 and quest 5 worked examples, one case per reward pattern, ordering, and that every shipped reward string parses.
- [x] 2.3 Add load validation for every failure listed in the spec (missing/empty source, unknown character, quest numbers not 1–5, no tasks, target < 1, unknown reward pattern, unknown equipment id, unknown shard unit id) and leave `heroId` unchecked (design D4); verify with `MythicQuestValidationTests`, one test per failure plus a passing test on the shipped source including `tauSmnDroneSniper`.
- [x] 2.4 Confirm the endpoint `GET /api/v1/game-catalog/mythic-quests` is served by the existing catalog route without auth and that no raw-file endpoint exists; verify with an API test alongside the existing catalog endpoint tests.

## 3. Release metadata, manifest and contract

- [x] 3.1 Bump `GameCatalogRelease.SchemaVersion` 4 → 5 and `Version` to the change's date tag (design D5); `GameVersion` stays `1.43`.
- [x] 3.2 Run `GameCatalogSnapshotTests`, review the diff (new `mythic-quests` entry, changed `mows` hash, schema/version fields only) and promote the snapshot.
- [x] 3.3 Build and check `artifacts/openapi/TacticusPlanner.Api.json`: if it enumerates dataset keys, `mythic-quests` is present; note the served `mythic-quests` and `mows.mythicAbility` shapes for the companion apps change `complete-library-unit-data`.

## 4. Refresh procedure (skill)

- [x] 4.1 Update `.agents/skills/game-catalog-data/SKILL.md` per design D6 (the `mows` mythic ability mapping, a new `mythic-quests` mapping line, and in the client-side companion bullet the ability-text generator re-run, relic ability icons and exact-case filenames), and copy it to `.claude/skills/game-catalog-data/SKILL.md`; verify `diff` reports the two copies identical.

## 5. Gates

- [x] 5.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, `git diff --check`, all passing.
- [x] 5.2 Live check via `aspire run` (wait for `api` and `api-migrations`): `GET /api/v1/game-catalog/mythic-quests` returns 117 records unauthenticated, the manifest lists `mythic-quests`, and `GET /api/v1/game-catalog/mows` shows `mythicAbility` on every record.
