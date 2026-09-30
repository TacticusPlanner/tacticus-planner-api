## Why

The apps' new Edit goal dialog has one Save that can change a goal's target, notes/farming preferences, project memberships, and priority. Today those are four endpoints (`PUT me/goals/{id}/target`, `PUT me/goals/{id}`, `PUT me/goals/{id}/projects`, `PUT me/goals/order`), so a single Save would be four sequential calls that can half-apply and force the client to track which steps landed. One atomic endpoint makes the save all-or-nothing.

## What Changes

- Add `PUT me/goals/{goalId}/edit`, an all-or-nothing edit that accepts any combination of four optional sections (target, details, projects, priority) and applies them in one locked transaction, returning the updated goal.
- Reuse, unchanged, the rules and error bodies of the existing endpoints: target validation and revision check, notes/strategy/locations/acquisition-source validation, project membership slot conflicts, and order revision conflicts. The existing four endpoints stay as they are (other clients and the project/goal reorder UIs use them).
- Priority is expressed as a target position among the account's Active/Paused goals; the server computes the reorder.
- Companion apps change: `goals-edit-dialog` (same name, `tacticus-planner-apps`); this API change applies first.

## Capabilities

### New Capabilities

- `goal-combined-edit`: the atomic edit endpoint, its sections, validation, conflict responses, and all-or-nothing guarantee.

### Modified Capabilities

(none)

## Impact

- `src/TacticusPlanner.Api/Features/Goals`: new `EditGoalEndpoint`; the target, details, projects and order mutation bodies of the four existing endpoints are extracted into transaction-scoped service methods (no HTTP response writing inside) that both the existing endpoints and the new one call.
- OpenAPI artifact (`artifacts`) regenerated; apps regenerate their client types.
- Tests: endpoint tests for each section, combinations, and rollback on a failing later section.
- No migration, no schema change.
