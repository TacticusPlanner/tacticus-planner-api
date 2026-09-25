## Why

`PLAN-010` needs distinct Rank milestones for one character in a project, while the current project slot index and API reject a second in-flight Rank goal regardless of target. `PLAN-011` also requires a deleted milestone to free its duplicate guard. V1 import currently merges different Rank targets, which would silently defeat the new model.

## What Changes

- Allow multiple in-flight Rank goals for one unit/project when normalized end targets differ; reject exact active/paused target duplicates with the existing goal identified.
- Keep non-Rank slot rules, status transitions, memberships, and concurrent creates atomic; delete/recreate remains a true new goal, not a restore.
- Preserve distinct V1 Rank targets instead of merging them, and return per-source outcomes.
- Provide stable target identity for later target editing and cross-project membership checks. **BREAKING:** project Rank slot uniqueness and V1 import merge semantics change.

## Capabilities

### New Capabilities

- `rank-milestones`: Distinct-target identity, conflict, lifecycle, and recreation behavior.

### Modified Capabilities

- `project-goal-slots`: Rank occupancy becomes target-specific while other types remain one-per-unit/type.
- `v1-goal-import`: Different Rank targets import as separate milestones instead of being merged.

## Impact

Goal/project membership validation, `ProjectGoal` constraint and EF migration, V1 import, endpoint tests, generated OpenAPI. Paired apps change `support-multiple-rank-milestones` handles independent ordering and overlap accounting. Depends on `integrate-level-progression-into-rank-goals` ownership decision; API applies before apps.
