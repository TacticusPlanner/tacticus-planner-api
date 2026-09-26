## Context

`GoalConfig` has separate Rank and Level target groups and `GoalType` includes Level. `CreateCombinedGoalsEndpoint` persists the client's chosen dependency graph; `V1GoalImportService` can synthesize Level prerequisites for Rank and Ability. The apps companion derives the required level from the rank ladder (and from the level an Ability target implies). This plan establishes the ownership decision before `support-multiple-rank-milestones` or `edit-goal-targets-in-place`.

## Goals / Non-Goals

**Goals:** Make the level a target needs an intrinsic, derived requirement of that target, and remove the Level goal kind so no path can plan the same level twice.

**Non-Goals:** Altering the XP curve, or hiding genuine eligibility blockers (absent player data, missing Unlock, insufficient Ascension). Preserving Level goals or interpreting legacy Level links.

## Decisions

1. **Level is not a goal kind.** Remove `GoalType.Level` and `GoalConfig.Level`/`LevelTarget`. Creation, combined creation, target editing, and import reject a Level goal type or target (400 for a request that names one). Alternative: keep Level goals for Ability or as standalone targets. Rejected because it recreates the duplicate-planning problem and a second owner of the same XP.
2. **The required level is derived, not stored.** For a Rank target it comes from the rank ladder (end rank plus partial upgrade slots); for an Ability target from the level the higher ability target implies, both against the current catalog. Nothing persists a duplicate level field, and no dependency edge represents it. Rank completion still requires the actual rank/slot target; reaching a level alone never completes anything.
3. **Existing Level goals are deleted by migration.** V2 is pre-production, so breaking changes are allowed. One EF migration deletes every Level goal (memberships cascade) and removes their ids from other goals' `depends_on`. Alternative: keep them readable but hidden. Rejected because it leaves dead data and a dead goal type. There is no legacy pair interpretation and no reversibility promise.
4. **V1 import and combined creation never synthesize Level.** Unlock and Ascension synthesis, ordering, and shortfall reporting are unchanged.
5. **No relational schema change beyond the data migration.** Goal config remains JSON; the migration is data-only (the model snapshot changes because the Level target group is dropped from the mapped config). The shared contract with the apps companion is the goal response (no `level` config, no Level `goalType`) and V1 import outcomes; if a new explicit relation is needed during apply, amend both paired artifacts before coding.

## Spec deltas to author

The API deltas are `rank-level-progression`, `v1-goal-import`, and `goal-target-editing` (drop Level from the supported target kinds and the all-kinds scenario). The API `goal-lifecycle-status` capability never mentioned Level, so it needs no delta here; the missing-Level reason lives in the apps `goal-blocker-reasons` capability.

## Risks / Trade-offs

- [Deletion is irreversible] → Acceptable pre-production; verify counts in the old-data migration test and state the deletion in the PR.
- [Ability level requirement drifts from the ladder] → Derive it in one place from the same rule creation used to suggest a Level prerequisite, and cover it with tests.
- [Clients still sending `Level`] → Fail with a clear 400; the apps companion ships in the same release train (API applies first).

## Migration Plan

Deploy API first: apply the data migration, then the code that no longer knows Level. Then ship apps. Rolling the code back without restoring data cannot recreate deleted Level goals.

## Open Questions

- Which rule exactly derives an Ability target's required level (the current creation-suggestion logic)? Confirm and reuse it during apply; it does not change the ownership decision.
