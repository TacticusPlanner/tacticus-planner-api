## Context

See proposal.md - Why. Today `ProjectGoalPlanningService` (`ProjectGoalPlanningService.cs`) enforces unit-grained ordering in three places:

- `NormalizeAsync` groups every in-flight `ProjectGoal` by `UnitKey` (entityType+entityId), orders each group internally via `OrderGoals` (a topological sort respecting `DependsOn`), then assigns sequential priorities group-by-group — so units always end up contiguous.
- `ApplyUnitOrderAsync` (backing `PUT /me/projects/{id}/unit-order`) validates the caller's submitted unit set against the project's current in-flight unit set (`requestedKeys.Count != grouped.Count`, etc.) and re-runs the same per-unit `OrderGoals` internally.
- `OrderGoals` is the shared within-unit topological sort both of the above call.

All mutation paths that touch priority (`CreateGoalEndpoint`, `CreateCombinedGoalsEndpoint`, `UpdateGoalStatusEndpoint`, `UpdateGoalProjectsEndpoint`, `UpdateProjectGoalsEndpoint`, `V1GoalImportService`) run through `ProjectGoalPlanningService.ExecuteLockedMutationAsync`, which serializes concurrent mutations against one project (the existing `project-goal-slots` concurrency guarantee) — this change keeps using that same lock, it doesn't touch it.

## Goals / Non-Goals

**Goals:**
- Replace the unit-keyed reorder endpoint with a goal-keyed one, preserving the existing "submit the complete current in-flight set, reject atomically if stale/incomplete" contract — just keyed by goal id instead of unit key.
- Remove automatic dependency-based ordering (`OrderGoals`) entirely — priority becomes whatever was last explicitly submitted (or append order), with no server-side reshuffling.
- Keep every other locked-mutation behavior (concurrency serialization, slot-conflict checking) exactly as-is.

**Non-Goals:**
- Not touching `(entityType, entityId, goalType)` in-flight slot uniqueness (`FindConflictAsync`, `project-goal-slots`) — untouched by this change.
- Not implementing Cluster 7's rank-range `DependsOn`-on-single-creation work (`add-rank-range-progression`) — a separate change, sequenced after this one.
- Not changing `ProjectGoal.Priority`'s storage shape — it's already a per-membership integer; only how it's computed/validated changes.

## Decisions

**Replace, don't add alongside.** `PUT /me/projects/{id}/unit-order` is removed outright and replaced by a new `PUT /me/projects/{id}/goal-order` accepting the project's complete ordered list of in-flight goal ids. Alternative considered: keep both endpoints (unit-order as a convenience wrapper that expands to goal ids server-side). Rejected — nothing in the new model still needs a unit-keyed operation, and keeping it would mean maintaining two ways to express one thing with no distinct use case for the old one.

**Validation mirrors the old unit-set check, just at goal granularity.** The new endpoint validates the submitted goal id set against the project's current in-flight (Active/Paused) goal id set — same size/membership comparison `ApplyUnitOrderAsync` already does for units, reused at goal granularity. A mismatch (stale client, concurrent membership change) is rejected atomically, same as today.

**Delete `OrderGoals` and `NormalizeAsync`'s per-unit grouping; keep `NormalizeAsync`'s two-zone renumbering.** `NormalizeAsync` still does a full renumbering pass on every mutation, exactly as today — it just drops the per-unit grouping and topological sort within it. Concretely: in-flight (Active/Paused) memberships are renumbered 1..N preserving their existing relative order (whatever the last explicit goal-order call set, with freshly created goals appended at the end of that order); historical (Completed/Archived) memberships are then renumbered N+1..M in their own stable prior order. This is the same two-zone shape `NormalizeAsync` already produces today (unit-grouping was applied only within the in-flight zone) — dropping unit-grouping does not change the zone boundary itself. Keeping the full-renumbering pass (rather than only touching newly created goals, which an earlier draft of this decision considered and rejected) is what keeps historical goals' priority values from ever drifting into the in-flight zone's numeric range as goals transition between statuses. `OrderGoals` (the topological sort) has no remaining caller and is deleted rather than left as dead code.

**No automatic dependency validation on reorder.** Per the resolved open question from CLUSTERS.md (option b): the new endpoint accepts any permutation of the submitted goal ids, including one that places a goal ahead of a `DependsOn` prerequisite it hasn't reached. This was a deliberate product decision, not an oversight — priority is now a pure ordering/scheduling preference; whether a goal can actually proceed is the existing, separate Restricted/Blocked signal (`goal-blocker-reasons`, apps side), computed independently of position.

**No schema change.** `ProjectGoal.Priority` already exists per membership as a plain integer; every currently stored value remains a valid position under the new model — dropping the "must be unit-contiguous" invariant doesn't invalidate any existing data, it just stops being enforced going forward.

**`UpdateProjectGoalsEndpoint` (membership replacement) stops accepting caller-authored priority.** Today it writes a raw, unvalidated caller-supplied `Priority` int straight onto the membership row, relying entirely on `NormalizeAsync`'s subsequent regrouping to reconcile whatever was submitted (duplicates, gaps, out-of-range values). That reconciliation goes away with the per-unit grouping. Alternative considered: add real validation to this endpoint's `Priority` field (reject duplicates/gaps) instead of ignoring it. Rejected — there is no remaining legitimate reason for this endpoint to accept a priority at all now that a dedicated goal-order endpoint exists; validating a field that should simply not exist is more code for a worse contract. The endpoint now ignores any submitted priority: an existing member's priority is left untouched, a newly added member is appended at the end of the in-flight zone (same as `CreateGoalEndpoint`), and `NormalizeAsync`'s renumbering pass (still called at the end of this endpoint, unchanged) produces the final values.

## Risks / Trade-offs

- **[Risk]** A consumer (Dailies calc, Insights, farming estimates) might implicitly assume unit-contiguous order beyond what it needs → **Mitigation**: per the companion apps proposal, these consumers only need "priority order" as a sort key, not contiguity — verify this with a repo-wide check for any code branching on unit adjacency before deleting `OrderGoals`, called out as its own task.
- **[Risk]** This is a breaking, unversioned API change (old clients calling the removed unit-order endpoint get 404) → **Mitigation**: V2 has no external API consumers besides its own `tacticus-planner-apps` client; deploy both halves of this change together rather than staggered.
- **[Risk]** Deleting `OrderGoals` removes the only code that ever validated a `DependsOn` chain doesn't cycle within a unit's priority order → **Mitigation**: cycle prevention for `DependsOn` itself is `CreateCombinedGoalsEndpoint`'s concern (indices must reference strictly earlier entries), not this ordering code's — confirmed no other caller relied on `OrderGoals` for cycle safety.
- **[Risk]** A goal transitioning between in-flight and historical status (e.g. Active → Archived, or vice versa) could leave its priority value in the wrong numeric zone, letting `ListProjectGoalsEndpoint`'s plain `OrderBy(Priority)` interleave a historical goal among in-flight ones — this is exactly what "Historical goals stay outside the in-flight ordering" (`project-management`) forbids → **Mitigation**: this is why `NormalizeAsync` keeps its full two-zone renumbering pass (see the Decision above) rather than only touching newly created goals — every mutation re-derives both zones from scratch, so a status transition always lands the goal in the correct zone's numeric range. `ListProjectGoalsEndpoint` itself needs no query-level status filtering because of this guarantee, exactly as today.
- **[Risk]** `UpdateProjectGoalsEndpoint`'s contract change (dropping caller-authored priority) is breaking for its one known caller (`add-goals-to-project-sheet.tsx` in the companion apps repo) → **Mitigation**: that caller already sends each existing member's own current priority back unchanged and only computes a value for newly added members — under the new behavior both of those submitted values are simply ignored with no functional difference for that caller; flagged in the companion apps change's own tasks to stop sending the field.

## Migration Plan

- No EF Core migration required.
- Deploy this change and the companion `tacticus-planner-apps` change together (same release) — the apps client must stop calling the removed unit-order endpoint in the same deploy that removes it.
- Rollback: revert both changes together. No data migration to unwind — stored `Priority` values are untouched by the deploy itself, only how future mutations compute/validate them changes.
