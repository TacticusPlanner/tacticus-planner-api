## Context

See proposal.md for motivation. The game catalog's `Data/**` is the committed source of truth, refreshed by
hand against V1's committed datamine output (the `game-catalog-data` skill: "There is no re-runnable
pipeline"). Raw datasets are discovered by convention (`foo-bar` → embedded `foo-bar.json`) and registered in
`GameCatalogDatasets`. Served views come from `GameCatalogDenormalizer`. Cross-reference validation runs over
the raw snapshot.

- `mows[]` lives inside the per-faction `units-{faction}.json`, and `BuildMows` copies each raw record and adds
  faction context. Adding an optional field to `GameCatalogMow` flows through with no denormalizer change.
- V1's mythic quest file has `quests[]` (117 characters × 5 quests, 1–4 tasks each, 1 755 reward strings) and
  `terms[]` (47 English-only task templates).
- Companion: `complete-library-unit-data` in `tacticus-planner-apps`. Shared contract: the served `mows`
  record (new `mythicAbility`) and the new served dataset `mythic-quests`.

## Goals / Non-Goals

**Goals:**

- Serve the MoW mythic ability and the mythic quest structure, both validated at load.
- Leave the refresh procedure (the skill) describing exactly where each new field comes from in V1, so the
  next game-version refresh keeps them.

**Non-Goals:**

- Task text templates or any translation: they stay client-side game text (see the apps design).
- Ability rules text for relics, MoW or mythic abilities: client-side game text, generated in the apps change.
- Re-deriving any other dataset or bumping `GameVersion` (the data is unchanged at 1.43).

## Decisions

**D1: `mythicAbility` is authored on the raw `mows[]` record, not joined from a separate file.** The raw MoW
record is already the hand-maintained copy of V1's MoW data; one extra `{ "name": "…" }` per record (11 edits)
keeps the source a single place. `GameCatalogMow` gains `GameCatalogMowMythicAbility? MythicAbility` (name
only). _Alternative:_ a separate `mow-mythic-abilities.json` joined in `BuildMows`. Rejected: a second file for
11 strings, and a join that can silently miss an id.

**D2: `mythic-quests` is a standalone raw + served dataset.** The raw `mythic-quests.json` mirrors V1's
`quests[]` minus the dropped fields (`characterName`, quest `name`, task `name`), with V1's reward **strings**
kept as is, so a refresh is a near-verbatim copy that diffs cleanly against V1. The denormalizer
(`BuildMythicQuests`) produces the served records: it renames `characterId` → `unitId` and `taskParameters` →
`parameters`, orders records by `unitId` and quests by `number`, and parses reward strings into typed records
(spec: Rewards are served as structured records). _Alternative:_ store the rewards already structured in the
raw file. Rejected: every refresh would need a hand transform; parsing once at load is cheap and validated.
_Alternative:_ serve the reward strings and let the client parse (as the apps do today). Rejected: the catalog
rule is structured ids and values, and the server can validate equipment and unit ids against the catalog.

**D3: Reward parsing is closed.** Six patterns, any other string fails load. A new reward kind in a future
patch then shows up as a startup failure during the refresh, not as an `unknown` chip in production.

**D4: `heroId` is not cross-validated.** Three V1 tasks name summons or NPCs as the hero, one of them
(`tauSmnDroneSniper`) not in the catalog's npcs. The client already names the hero with a fallback; validating
would force a fake NPC or a special case for no benefit.

**D5: Release metadata.** Bump `SchemaVersion` 4 → 5 (a served shape changes and a dataset is added) and
`Version` to the change's date tag. `GameVersion` stays `1.43`.

**D6: Skill update.** In `.agents/skills/game-catalog-data/SKILL.md` (and the identical `.claude/skills/` copy):

- the **mows** mapping line gains `mythicAbility` ← V1 `mow/data/new-mows-data2.json` `mythicAbilities[0]`,
  matched by `id`;
- a new **mythic-quests** mapping line: V1 `mythic_quests/data/mythic-quests.json` `quests[]`, the fields
  dropped, rewards kept as strings, and `terms[]` going to the apps `mythicQuestTasks` namespace (English
  only, no other source);
- the **client-side companion** bullet gains: re-run the apps ability-text generator (named in the apps
  design), relic ability icons `abilities/ui_icon_ability2_{abilityId}.png`, and asset filenames that match
  the catalog id exactly, including case.

### Manifest and tests

- The manifest gains `mythic-quests`; the `mows` hash changes. Promote the API-level Verify snapshot
  (`tests/TacticusPlanner.Api.Tests/GameCatalogSnapshotTests`).
- `TacticusPlanner.GameCatalog.Tests`: a `MythicQuestDenormalizerTests` (shape, ordering, reward parsing per
  pattern, Abaddon worked example), `MythicQuestValidationTests` (each failure in the spec), and a MoW test
  for `mythicAbility` (served for all 11, validation failure).
- No EF Core migration: no persisted schema changes.

## Risks / Trade-offs

- [The raw quest file is a large hand-copied blob (117 × 5)] → Port it with a throwaway script run once and
  checked in only as data. Verify by the spec's counts and by a test that every V1 reward string round-trips
  through the parser.
- [The client still ships its static copy until the apps half lands] → Apply order API → apps. Old clients
  ignore the extra dataset and the optional field, so the API can deploy first.
- [`SchemaVersion` bump forces clients to resync the whole catalog once] → Acceptable; same as every prior
  shape change.
