## Why

`GP-38` and `PLAN-012` require users to adjust a target without deleting and recreating a goal. The existing `PUT /me/goals/{id}` intentionally edits notes/sources/strategy only; replacement loses goal id, history, memberships, and priority.

## What Changes

- Add a dedicated, revision-checked target update for Rank, Ascension, Level, Ability, and Upgrade goals; Unlock has no adjustable target.
- Validate the new target and dependency/slot constraints, update Rank target occupancy atomically, preserve id/status/memberships/order and creation snapshot, and append target-change history.
- Return current target/revision and structured stale/conflict errors so the client can recover without silently overwriting another edit. **BREAKING:** targets are no longer immutable after creation; goal detail adds revision and target-change history.

## Capabilities

### New Capabilities

- `goal-target-editing`: Supported target mutations, validation, concurrency, identity preservation, and history contract.

### Modified Capabilities

None; existing target-model rules continue to validate target shape, and this adds an edit operation.

## Impact

API goals endpoint/DTOs, `GoalConfig`, event history, target validator, project Rank keys from `support-multiple-rank-milestones`, tests, generated OpenAPI; possible EF migration if event payload shape requires one. Paired apps change `edit-goal-targets-in-place` consumes the contract. Apply API first after Rank/Level and multi-Rank changes.
