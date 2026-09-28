## Why

`PLAN-009` needs one reviewed add/remove save for many goals. The existing project-side `PUT /me/projects/{id}/goals` is atomic, but a client replacing a list based on stale membership can erase another edit without conflict feedback. The bulk UI also needs the exact goals whose removal would violate the last-membership rule.

## What Changes

- Extend project membership replacement with an expected membership set checked under the existing project lock; reject stale saves atomically with a structured conflict and current set.
- Return structured last-membership feedback naming blocked goal IDs, while preserving project slot conflict behavior.
- Keep membership replacement independent of the account-wide goal order (`Goal.GlobalPriority`, established by `establish-global-goal-priority`) and retain one transaction for the whole batch. The endpoint already writes no order; this change pins that as a requirement and corrects the operation's stale OpenAPI summary ("…and priority ordering").

## Capabilities

### New Capabilities

- `project-bulk-membership-replacement`: Atomic, conflict-aware whole-project membership updates.

### Modified Capabilities

None; project goal-slot invariants remain unchanged.

## Impact

API `UpdateProjectGoalsEndpoint`, endpoint tests, generated OpenAPI; paired `tacticus-planner-apps` change with the same name consumes the contract. Applies after `establish-global-goal-priority` (its `GlobalPriority` projection is the response shape). No database migration is expected. This V2 contract adjustment is allowed by the workspace destructive-change policy.
