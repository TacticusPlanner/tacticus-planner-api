## Context

`GoalConfig` calls target groups immutable, `UpdateGoalEndpoint` edits only non-target fields, `Goal.Revision` exists as an EF concurrency token but is not in detail responses, and `GoalEvent` stores only type/time. The API companion for `support-multiple-rank-milestones` adds a denormalized Rank target key on memberships.

## Goals / Non-Goals

**Goals:** One atomic target mutation preserving goal identity and order, with explicit concurrency/history and fresh downstream calculations.

**Non-Goals:** Editing an Unlock's absence-of-target, retargeting completed history, replacing a goal row, or changing creation baseline/snapshot.

## Decisions

1. Use dedicated `PUT /me/goals/{id}/target` with a target-group payload and `expectedRevision`, leaving existing general-update null semantics untouched. Return `revision` in detail responses. Alternative: add optional target fields to the existing notes/sources PUT. Rejected because omitted-versus-clear semantics and concurrency become ambiguous.
2. Validate end targets relative to the stored start/baseline and catalog, not current synced progress. This permits shortening to an already attained milestone without auto-completing a stored Active/Paused status. Reject unsupported lifecycle states and any malformed mixed target-group payload.
3. Within the goal/project lock and one transaction, check every project's Rank target occupancy, update `Goal.Config`, update denormalized membership keys, increment revision, and append `TargetChanged` event. Non-Rank edits need no slot-key rewrite but still use revision precondition. Alternative: delete/create. Rejected because it loses id/history and can reorder memberships.
4. Add optional old/new target data to the existing JSON event payload; keep `GoalSnapshot` as immutable creation-time evidence. Live requirements/estimates are derived from current target and player/catalog data, not snapshot. No relational schema migration is expected for JSON payload evolution; run EF model/migration check and include a migration in this change if EF generates a schema change.
5. Paired `tacticus-planner-apps` change consumes target update, revision, and event fields; it must invalidate Goals/Projects/Insights/Dailies caches after save.

## Risks / Trade-offs

- [Existing event readers assume only type/time] → Make old/new fields optional and add mapper compatibility tests for old JSON rows.
- [Concurrent membership or target edits race] → Lock affected projects, re-read revision/slots, and reject stale/conflicting saves atomically.
- [A target shrink leaves a stale prerequisite edge] → Preserve edge history but recompute whether its predicate still blocks; never let an irrelevant edge force a false blocker.

## Migration Plan

No planned relational migration. Existing goal/event JSON remains readable with optional event payload fields. Deploy API first; older clients continue using the general update endpoint while new clients use target PUT. Rollback leaves harmless TargetChanged events and the latest target; do not erase user edits.

## Open Questions

- Which existing endpoint error envelope best carries the current goal detail on stale revision? Keep the required 409 payload semantics; choose serialization consistently with other goal conflicts during implementation.
