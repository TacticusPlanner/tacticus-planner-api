## Why

`PLAN-008` reports redundant Level goals and misleading Restricted Rank goals. The present combined-creation and V1 import flows can create a separate Level prerequisite for routine Rank progression, although Rank's target already determines the necessary level gate.

## What Changes

- Treat the level needed by a Rank target as an intrinsic requirement of that Rank goal, not an automatically created Level goal or dependency.
- Preserve independent Level goals and Level prerequisites for Ability targets where needed.
- Reconcile legacy Rank→Level dependency pairs without deleting ambiguous user-authored Level goals; do not let a routine level gate masquerade as a separate blocked goal. **BREAKING:** Rank prerequisite creation/import semantics change.

## Capabilities

### New Capabilities

- `rank-level-progression`: Rank target's intrinsic level requirement and legacy pair interpretation.

### Modified Capabilities

- `v1-goal-import`: V1 Rank import no longer synthesizes a separate Level prerequisite solely for Rank.

## Impact

API goal validation/combined creation and V1 import, prerequisite representation, endpoint tests and OpenAPI if response semantics change. No schema migration is planned. Paired `tacticus-planner-apps` change of the same name updates creation and planning presentation; API applies first.
