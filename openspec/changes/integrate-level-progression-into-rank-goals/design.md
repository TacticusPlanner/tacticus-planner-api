## Context

`GoalConfig` has separate Rank and Level target groups. `CreateCombinedGoalsEndpoint` persists the client's chosen dependency graph; `V1GoalImportService` can synthesize Level prerequisites for Rank. The apps companion derives required level from the rank ladder. This plan establishes the ownership decision before `support-multiple-rank-milestones` or `edit-goal-targets-in-place`.

## Goals / Non-Goals

**Goals:** Keep one Rank milestone for routine rank-plus-level progression and avoid duplicate cost/status from legacy pairs.

**Non-Goals:** Deleting ambiguous Level records or removing user-authored Level and Ability-level goals.

## Decisions

1. Derive the level gate from the Rank end/partial-slot target and current catalog ladder; do not persist a duplicate Rank-level field. Alternative: retain a linked Level goal as the authoritative level requirement. Rejected because it creates a separately ordered milestone for routine Rank work.
2. Manual combined creation stops adding Level solely for Rank. V1 import follows the same rule, but can still synthesize Level for Ability. API validates the submitted dependency graph and interprets legacy edges; it does not silently delete stored Level goals.
3. Treat a legacy Level with exactly one Rank dependent as absorbed into that Rank for effective planning, but retain its id and data. If any Ability dependent exists, keep the Level independently actionable and allocate its XP once. Alternative: hard-delete every linked Level. Rejected because provenance is absent and intentional/shared goals could be lost.
4. No EF migration is required: existing JSON target/dependency columns can represent the new behavior. The shared contract with the apps companion is existing goal responses and V1 import outcomes; if a new explicit relation is required during apply, amend both paired artifacts before coding.

## Risks / Trade-offs

- [A Rank-only linked Level may have been intentionally authored] → Preserve it in storage and detail-by-id, and test reversibility of the plan projection.
- [Ability and Rank share a Level] → Give the shared Level one allocation owner and never credit XP twice.
- [Current screenshot may be stale] → Reproduce on the current stack before editing; the new creation/import contract still stands as the chosen model.

## Migration Plan

Deploy API behavior before apps. Reconcile legacy pairs in read/plan projection without destructive data migration, then update import and combined-creation regressions. Rollback restores prior interpretation without data loss.

## Open Questions

- Which real pre-change Rank/Level pair best exercises the shared-Ability case? Capture an anonymized fixture for both repos before apply; it does not alter the ownership rule.
