## Why

`PLAN-008` reports redundant Level goals and misleading Restricted Rank goals. A separate Level goal is the wrong model altogether: the level a character needs is a property of the target it serves (a Rank target, an Ability target), not an independently planned milestone. Keeping a Level goal kind forces duplicate XP planning, extra dependency edges, and a Restricted state for ordinary progression.

## What Changes

- **BREAKING:** Remove the Level goal type. The API no longer accepts, stores, or returns a Level goal or a Level target group, and no goal may depend on one.
- Treat the level needed by a Rank target, and by an Ability target, as an intrinsic requirement of that goal, derived from the target and the catalog ladder, never a created goal or dependency.
- Delete existing Level goals in a data migration and strip their ids from other goals' dependency lists. V2 is pre-production, so this destructive change is allowed and no legacy pair interpretation is kept.
- V1 import and combined creation never synthesize a Level goal; Unlock and Ascension prerequisite rules are unchanged.

## Capabilities

### New Capabilities

- `rank-level-progression`: A Rank or Ability target's intrinsic level requirement, and the removal of the Level goal type.

### Modified Capabilities

- `v1-goal-import`: Imported Rank/Ability targets no longer synthesize a Level prerequisite; synthesized prerequisites are Unlock and Ascension only.
- `goal-lifecycle-status`: The missing-Level prerequisite reason no longer exists. (Delta spec still to be authored; see design, "Spec deltas to author".)
- `goal-target-editing`: Level is no longer a supported target kind. (Delta spec still to be authored.)

## Impact

API goal validation, combined creation, target editing, V1 import, `GoalType`/`GoalConfig` and their EF mapping, the blocker-reason contract, endpoint tests, generated OpenAPI, and one EF data migration deleting Level goals. Paired `tacticus-planner-apps` change of the same name removes the Level goal from creation and presentation; API applies first.
