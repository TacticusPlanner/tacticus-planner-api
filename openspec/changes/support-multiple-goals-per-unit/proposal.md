## Why

UserJot "Support Multiple Rank Goals per Unit" asks for V1 parity. In V1 a unit can carry any number of goals of any type (G1 now, D3 later, A1 eventually; Legendary now, Mythic later), each placed at its own priority. V2 already allows distinct Rank milestones (`rank-milestones`), but every other goal type is still one in-flight goal per unit and type. That has two effects. Ascension, Ability and Upgrade can't be staged the way Rank can. And a second Rank milestone that needs more rarity collides with the unit's existing Ascension goal, because the create flow can only add a new Ascension goal and that slot is taken. V1 import also merges every non-Rank duplicate, so distinct V1 Ascension/Ability/Upgrade goals are lost.

## What Changes

- Generalize Rank's "one in-flight goal per normalized end target" rule to every goal type except Unlock. Ascension, Ability (Character and Machine of War) and Upgrade goals each get a canonical target key, and distinct keys MAY coexist for one unit. Exact duplicates are still rejected with `slotOccupied`. Unlock stays one per unit.
- A combined creation request MAY carry several specs of the same goal type for one unit when their target keys differ, so the client can create a milestone ladder (e.g. Rank Gold1 → Diamond1 → Diamond3 → Adamantine1, each depending on the previous) in one atomic request.
- A combined spec MAY declare dependencies on the unit's existing in-flight goals (`dependsOnGoalIds`), not only on earlier specs in the same request. An Ascension goal reached this way lifts the progression floor for cap validation exactly as an in-request Ascension does.
- Target edits re-check the slot for every keyed type, not only Rank.
- `slotOccupied` carries `normalizedTarget` for every keyed type.
- V1 import preserves distinct targets of every keyed type as separate goals in V1 priority order. Only exact target duplicates merge. Prerequisite synthesis reuses an existing or imported Ascension goal that already covers the need.
- **BREAKING:** slot identity changes for Ascension/Ability/Upgrade; the combined-request rule "each goal type at most once" is removed; V1 import merge semantics change.

## Capabilities

### New Capabilities

- `goal-target-milestones`: canonical target key per goal type, distinct-target coexistence and lifecycle for Ascension/Ability/Upgrade (Rank keeps `rank-milestones`).
- `goal-dependencies`: dependencies on existing in-flight goals at creation, and their effect on cap validation.

### Modified Capabilities

- `project-goal-slots`: slot identity becomes `(entityType, entityId, goalType, targetKey)` for every type except Unlock.
- `goal-target-editing`: slot re-check on edit applies to every keyed type.
- `v1-goal-import`: distinct targets of every keyed type import separately; exact duplicates merge.

## Impact

`GoalTargetKey` (replaces/extends `RankTargetKey`), `ProjectGoalPlanningService.SameSlot`, `CreateCombinedGoalsEndpoint` (+ validator, `CombinedGoalSpec.DependsOnGoalIds`), `GoalEditing`, `GoalProblems`, `V1GoalImportService`, endpoint/integration tests, regenerated `artifacts/openapi`. No EF migration: the slot rule is enforced in code under the profile lock and `dependsOn` is an existing `uuid[]` column.

Companion apps change: `support-multiple-goals-per-unit` (same name in `tacticus-planner-apps`): allocation of overlapping Ascension progression, milestone-aware prerequisite suggestions, ladder creation and display. The API half applies first. Builds on the archived `rebuild-foundation-pre-launch` (account-scoped slots, profile lock).
