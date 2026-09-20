## Why

Project priority is currently stored and reordered at unit granularity: every in-flight goal for a unit is forced into one contiguous block, and reordering means dragging whole unit blocks in a dedicated Reprioritize Units dialog. A 2026-09-19 product decision (Cluster 4/7 of the feedback backlog) replaces this with flat per-goal priority — every goal is independently positionable, with no unit grouping in storage or ordering — because it's a simpler mental model for users and it's what makes Cluster 7's "rank range" goals (two real, `DependsOn`-linked goals of the same type for one unit, letting a player push a unit partway now and further later) actually useful: under the unit-grained model they could only ever sit adjacent to each other, unable to have another unit's goal placed between them.

## What Changes

- **BREAKING**: Replace the unit-keyed `PUT /me/projects/{id}/unit-order` operation with a goal-keyed reorder operation that accepts the project's complete ordered list of goal ids (not unit keys).
- **BREAKING**: `ProjectGoalPlanningService.NormalizeAsync` stops grouping in-flight goals by unit (`GroupBy(UnitKey.From)`, topological sort within each group). Priority becomes a flat, per-goal list; a goal's position is whatever the client submitted it as.
- **BREAKING**: Dependency order is no longer automatically enforced or computed on a goal's position. A goal may be positioned anywhere in the list regardless of whether a goal it `DependsOn` has been reached — priority becomes a pure ordering/scheduling preference, fully decoupled from dependency validity. Blocking (the existing Restricted/Blocked distinction) remains a separate, unaffected signal.
- New goal creation still appends at the end of the project's flat priority list (unchanged in spirit — no caller-authored numeric priority — just no longer "into unit order").
- **`UpdateProjectGoalsEndpoint` (`PUT /me/projects/{id}/goals`, membership replacement) stops accepting caller-authored priority.** Today it accepts a raw `Priority` int per goal with no validation, relying on `NormalizeAsync`'s regrouping to silently reconcile whatever is submitted; once that regrouping is removed, an unvalidated caller-supplied priority would persist as-is. It now ignores any submitted priority: an existing member keeps its current stored priority regardless of what's submitted, and a newly added member is appended after the project's current goals — mirroring `CreateGoalEndpoint`'s existing append behavior.
- Out of scope: the `(entityType, entityId, goalType)` in-flight slot-uniqueness invariant (`project-goal-slots`) is untouched by this change — that's `add-rank-range-progression`'s concern (Cluster 7/GP-05), which depends on this change landing first only for the ordering half to make sense, not for the slot logic itself.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `project-unit-ordering`: comprehensively rewritten, not incrementally revised — all 4 existing requirements ("Project unit order is addressable through a dedicated operation", "Goals inside a unit are ordered automatically", "Flattened order remains the canonical scheduler order" [unit-contiguous], "Goal creation does not accept numeric priority" [unit-order framing]) are being replaced by goal-level equivalents, plus a new requirement extending "no caller-authored priority" to membership replacement (`UpdateProjectGoalsEndpoint`). Worth considering a rename to `project-goal-ordering` during the specs step, since "unit" no longer describes what's being ordered — not decided here.
- `project-goal-slots`: two of its "Concurrent mutations in one project serialize without failing" scenarios assert unit-block phrasing ("both goals appear in that unit's block", contiguous "unit blocks") that becomes inaccurate once unit grouping is removed — needs a small wording delta, not a behavior change.
- `v1-goal-import`: "Imported goals preserve V1 unit order" is built entirely on "a project orders whole unit blocks" — its stated reason for collapsing V1's interleaved priority into unit blocks and discarding the original interleaving. That reason no longer holds, and flat per-goal ordering can now preserve V1's actual priority sequence exactly, which is a strictly more faithful import than today's collapsed version — this requirement is rewritten accordingly, not just re-worded.

## Impact

- **API**: `ProjectGoalPlanningService` (`NormalizeAsync`, `ApplyUnitOrderAsync` → replaced, `OrderGoals` → removed or repurposed), the `unit-order` endpoint and its request/response DTOs, and any EF query that currently assumes unit-contiguous ordering when reading a project's goal list.
- **Consumers**: Dailies, Insights, and farming estimates read the project's ordered goal list today under a "unit order" framing (`project-management`'s "Unit order drives priority-sensitive calculations" requirement, apps side) — they need no calculation change (still just "priority order"), but their spec language needs the same unit→goal reframing as the companion apps change.
- **Companion change**: `add-inline-goal-reprioritize` in `tacticus-planner-apps` (apps half — UI reorder surfaces, `project-management` spec delta). This api change should apply first.
- **No database schema change**: `ProjectGoal.Priority` already exists per-membership; this changes how it's computed/validated, not its shape.
- **Level-goal display merge (folded into the companion apps change) is apps-only**: the required level and current character level are both already available client-side (same data the create-time prerequisite suggestion already uses), so no API surface is needed for it. Noted here only so this change's scope isn't read as covering it too.
