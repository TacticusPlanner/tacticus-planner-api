## Context

`ProjectGoal` is the many-to-many membership and owns per-project numeric priority. `ListProjectGoalsEndpoint` sorts by it, and downstream clients feed that order to priority-sensitive schedulers. Goal identity/type/status live on `Goal`. A partial unique index on `goals` currently enforces one Active/Paused goal per profile/unit/type, independent of project membership.

The new invariant crosses tables, so it cannot be represented by merely changing the existing index columns. Goal target identity/type are immutable after creation, and the domain model SHALL prevent post-creation changes to `Goal.EntityType`, `Goal.EntityId`, and `Goal.GoalType`. That keeps membership denormalization safe. Goal lifecycle remains global; project-specific pause/resume is out of scope.

## Goals / Non-Goals

**Goals:**

- Expose project ordering in unit terms while retaining a canonical flattened goal order for existing engines.
- Enforce one Active/Paused goal type per unit per project under concurrency.
- Allow separate in-flight instances in non-overlapping projects.
- Preserve canonical goals shared across projects.
- Remove independent equipment goals and ambiguous material-target naming.
- Return actionable structured conflicts.

**Non-Goals:**

- Persisting a standalone unit-plan entity or empty unit groups.
- Manual within-unit goal ordering.
- Project-local goal lifecycle status.
- Reintroducing equipment goals or altering equipment catalog/player data.
- Changing farming or attainment formulas.

## Decisions

**1. `ProjectGoal.Priority` remains the persisted scheduler order.**

No new ProjectUnit table is introduced. Unit groups are derived from current in-flight memberships. The unit-order endpoint receives every distinct Active/Paused Character/MoW unit key exactly once, validates the set against current membership under a project-row lock, computes automatic inner order, then writes spaced priorities across the flattened in-flight list. Completed/Archived memberships follow afterward in stable prior order.

This keeps every existing consumer compatible with ordered project-goal responses while making unit order the only user-authored concept.

**2. Unit ordering has a dedicated endpoint.**

`PUT /api/v1/me/projects/{projectId}/unit-order` accepts:

```json
{
  "units": [
    { "entityType": "Character", "entityId": "ragnar" },
    { "entityType": "Mow", "entityId": "forgefiend" }
  ]
}
```

The endpoint locks the owned project row for the transaction, rejects missing/duplicate/unknown/currently stale unit sets, rewrites priority only, and returns `ProjectGoalsResponse` as `{ "goals": [{ "goalId": "...", "priority": 1 }] }`. It never replaces membership, and no alternative ordered-unit-summary response is exposed.

- _Alternative:_ reuse `PUT /projects/{id}/goals`. Rejected because that endpoint replaces membership and can drop concurrently added goals when the user's intent is only ordering.

**3. Automatic inner order is dependency-first and stable.**

For one unit, perform a stable topological sort of `DependsOn` edges restricted to goals in the unit. Among simultaneously available nodes, use prior priority then goal id. A new goal is assigned within its unit after its prerequisites and otherwise after existing unit goals. Invalid dependency cycles fail validation rather than producing nondeterministic order.

**4. Numeric priority is removed from creation contracts.**

`ProjectPriorityRequest` becomes project identity only (and should be renamed accordingly). Single/combined creation resolves membership first, inserts into an existing unit block or creates a new block at the end, then normalizes the affected projects. Combined goals are internally dependency-ordered automatically.

**5. Project-scoped slots are enforced on `project_goals`.**

Add denormalized columns:

- `EntityType`
- `EntityId`
- `GoalType`
- `OccupiesInFlightSlot`

Target columns are copied from immutable `Goal` fields whenever membership is created. `OccupiesInFlightSlot` is true exactly when the owning goal is Active/Paused and is updated across all memberships in the same transaction as lifecycle transitions. Add a partial unique index on `(ProjectId, EntityType, EntityId, GoalType)` where `OccupiesInFlightSlot = true`.

The immutable slot fields are initialized only by the goal creation path. Public update operations cannot replace them; changing a target means creating a replacement goal.

Friendly pre-checks provide project-aware errors; the index is the race backstop. Constraint exception mapping returns the same structured conflict contract.

- _Alternative:_ application checks plus advisory locks only. Rejected because correctness would depend on every future write path acquiring the same lock convention.
- _Trade-off:_ lifecycle state is duplicated as a derived boolean. Accepted because transition paths are centralized and the index makes the desired invariant directly enforceable.

**6. Every membership-changing operation validates slots.**

Before mutation, determine the projects newly occupied by every Active/Paused goal and find another occupying membership with the same slot. Applies to single/combined create, `UpdateGoalProjects`, `UpdateProjectGoals`, and transition into Active/Paused. Removing membership or transitioning to Completed/Archived frees slots. The same canonical goal shared across projects occupies one slot in each but never conflicts with itself.

Conflict responses include project id/name, entity type/id, goal type, and existing goal id. Operations are atomic: if any target project conflicts, none of the requested memberships/status changes are applied.

**7. Independent equipment goals are deleted, not deprecated.**

Remove `GoalEntityType.Item`, `GoalType.UpgradeItem`, `GoalConfig.Item`/`ItemTarget`, request/response mapping, target validation, and equipment-goal endpoint tests. Migration deletes goals with either removed discriminator before code reads them and cascades `project_goals`.

Ordinary `GoalType.Upgrade` remains Character/MoW-only. Rename its material element types to `UpgradeMaterialTarget`, `UpgradeMaterialTargetRequest`, and `UpgradeMaterialTargetResponse`; JSON fields remain unchanged.

**8. Migration sequence preserves remaining goals.**

Within one migration:

1. Delete independent equipment goals (membership cascades).
2. Add nullable slot columns to `project_goals`.
3. Backfill slot identity and `OccupiesInFlightSlot` from joined goals.
4. Make slot columns required.
5. Drop `ix_goals_one_active_or_paused_per_entity_and_type`.
6. Add the partial unique project-slot index.

Because V2 is pre-production, no compatibility reader or deprecated enum value remains.

## Risks / Trade-offs

- **Derived slot flag drift:** all goal-status writes must update memberships. Mitigation: centralize synchronization in a service and test every transition; consider a save interceptor only if centralized calls prove insufficient.
- **PostgreSQL-only behavior:** EF InMemory does not enforce partial unique indexes or row locking. Mitigation: retain handler tests and add proportional PostgreSQL integration coverage for the concurrency backstop.
- **Concurrent reorder/membership mutation:** project-row locking plus exact unit-set validation serializes ordering with membership-changing operations that acquire the same lock.
- **Historical goal placement:** Completed/Archived memberships are not user-prioritized. Preserve their relative order after in-flight groups so filters remain deterministic.
- **Breaking contract:** allowed by V2 policy; regenerate OpenAPI and coordinate apps before merge/deploy.

## Migration Plan

Implement API/persistence first, generate and commit OpenAPI, then update the apps submodule against that artifact. Apply the migration through the normal API startup/Aspire migration resource. Equipment-goal rows are intentionally unrecoverable except by database backup; no production data exists under the current V2 policy. Rollback requires reverting code and restoring/recreating removed local test data rather than a compatibility downgrade path.
