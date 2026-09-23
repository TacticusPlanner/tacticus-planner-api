## Why

Priority currently exists inside each project, so the same goal can have competing positions and daily planning depends on which project is selected. The account needs one explicit, durable order for all in-flight goals, with projects acting as organization rather than alternate execution plans.

## What Changes

- Introduce one account-wide order of Active and Paused goals and a revision-checked reorder operation independent of memberships or unit type.
- Make global and project goal reads expose that order; project lists become filtered projections.
- **BREAKING**: Retire project-scoped reorder and per-membership priority as writable planning contracts. Project membership and Current plan selection no longer change execution priority.
- Migrate existing orders deterministically, putting the former Current plan's order first and appending remaining in-flight goals once each; preserve historical goals and statuses.
- Normalize priority on creation, status transitions, and deletion, with race-safe concurrent writes.

## Capabilities

### New Capabilities

- `global-goal-priority`: Canonical account-wide order, mutation, lifecycle, and migration contract.

### Modified Capabilities

- `project-unit-ordering`: Project reads and membership operations become projections of global priority; project-scoped reorder is retired.

## Impact

- Companion apps change: `tacticus-planner-apps/openspec/changes/establish-global-goal-priority`; apply API first.
- Affects Goal/Profile persistence, EF migration, goal/project endpoints, import and ordering services, OpenAPI artifact, and consumers of project priority.
- Keep at least one membership per goal (Default project remains an unobtrusive filing fallback); Current plan remains a browsing preference, not an execution context.
