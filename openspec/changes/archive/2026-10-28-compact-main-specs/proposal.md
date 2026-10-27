# Proposal

## Why

The 67 main specs (330 requirements) have built up duplication as changes were archived. The same rule is stated in several places, the same scenarios are copied under several requirements, and some text only made sense inside the delta that introduced it ("as before this change", one-off migration requirements, scenarios that compare against retired files). Because of this, a reader can't tell which copy is canonical, and the next change has to edit several copies or risk them drifting apart. Companion change: `compact-main-specs` in `tacticus-planner-apps` does the same for the frontend specs. The two halves are independent and neither one blocks the other.

## What Changes

This is spec text only. No served dataset, endpoint, contract or schema changes. Each item is traceable to an audit id in `design.md` (D = specified twice, S = repeated scenario, L = archive leftover, P = Purpose edit).

- **Give catalog boilerplate one home (D1).** Add one requirement to `game-catalog-http-caching`: every served dataset is anonymous, in the shared envelope, hashed in the manifest, its raw source is not served, and it carries ids only. Strip the per-dataset copies of that rule from eight dataset specs.
- **Remove requirements specified twice (D2–D8, D11–D14, D16).** Keep the canonical copy and drop the restatement. In-spec repeats in `guild-raid-current-status`, `rank-milestones` and `project-goal-slots` get the same treatment.
- **Retire `project-unit-ordering` (D15).** Every requirement in it is covered by `global-goal-priority`, `goal-collection-read` or `project-bulk-membership-replacement`, and two of them are stale against those specs. All four are REMOVED and the capability folder is deleted.
- **Drop repeated scenarios (S2–S9).** Where a scenario is a near-verbatim copy of one kept elsewhere, drop the copy.
- **Remove archive leftovers (L1–L25).**
  - REMOVED: one-off migration or transition requirements ("Adding X does not bump the schema version", "Existing Level goals are removed by migration", and so on).
  - Replaced: scenarios that compare against retired raw files get steady-state scenarios.
  - Reworded: "before/after this change" and "unchanged from prior behavior" phrasing.
- **Rewrite transition-flavoured Purpose paragraphs (P1–P4)** by direct edit, because deltas cannot change Purpose.

Not in scope: audit findings where the two copies **differ in substance** (D9 acquisition-source update scope, D10 cap from existing Ascension dependencies), stale raw-file names (L26, L27), and the four inconsistencies listed in `design.md`. Fixing those changes meaning, not just duplication, so each needs its own change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `game-catalog-http-caching`: adds the single catalog-wide "public, listed, ids only" requirement (D1).
- `game-catalog-datamine-source`: removes the one-off "changes no served data" requirement (L1). Adds "The catalog game version is the pinned datamine's build", which keeps the `GameVersion` rule that until now lived only in a removed raid-bosses requirement (L5).
- Dataset specs that lose duplicated boilerplate or leftovers: `character-ability-costs-dataset`, `mythic-quests-dataset`, `npcs-dataset`, `raid-bosses-dataset`, `game-shops-dataset`, `unit-power-dataset`, `horde-mode-tiers-dataset`, `campaign-battles-dataset`, `ascension-costs-dataset`, `characters-dataset`, `equipment-dataset`, `mow-upgrade-costs-dataset`, `mows-dataset`, `upgrades-dataset`, `quest-events-dataset`, `guild-raid-meta-dataset`.
- Datamine and LRE specs: `legendary-event-datamine-source`, `legendary-event-run-schedule`, `legendary-event-reward-ladder`, `legendary-event-plans`, `home-screen-event-scoring`.
- Goal and project specs: `goal-target-model`, `goal-combined-edit`, `goal-bulk-status`, `global-goal-priority`, `rank-milestones`, `rank-level-progression`, `project-goal-slots` (two requirement renames), `project-bulk-membership-replacement`, `project-unit-ordering` (all requirements removed and the capability retired), `default-project`, `v1-goal-import`.
- Other: `guild-raid-current-status`, `openapi-contract`, `onslaught-progress-overrides`, `player-data-sync`.
- Scenario edits only, with no delta file: `game-events-calendar-dataset`, `goal-target-milestones` and `legendary-event-lane-points`. Their edits are in `direct-edits.md` (see design.md).

## Impact

- Only `openspec/specs/**` changes, applied through archive plus a few direct Purpose edits. Code, migrations, OpenAPI and the manifest snapshot are untouched.
- 20 requirements are REMOVED and 3 are ADDED, so the count goes from 330 to 313. 42 are trimmed through MODIFIED and 2 are RENAMED. 46 scenario drops and renames are listed in `direct-edits.md`.
- Specs that cite a removed requirement by name are re-pointed in the same change. `project-unit-ordering` has no inbound references.
