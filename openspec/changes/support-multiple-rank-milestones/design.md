## Context

`ProjectGoalConfiguration` currently has a partial unique index on project/unit/type for every in-flight goal. `ProjectGoalPlanningService` enforces it through every creation, membership, status, and reorder path. `DeleteGoalEndpoint` hard-deletes a goal and cascades memberships. V1 import groups by unit/type and merges Rank targets. The apps companion owns need allocation and UI.

## Goals / Non-Goals

**Goals:** Target-specific Rank occupancy with atomic conflict behavior, and lossless distinct-target import.

**Non-Goals:** Multiple identical active Rank targets in one project, changes to non-Rank uniqueness, or per-project priority replacing eventual global order.

## Decisions

1. Normalize the end target into one canonical key of end rank plus applied-slot count, with point-five translated to its equivalent count. Keep start and strategy out of identity. Alternative: compare entire `GoalConfig` JSON. Rejected because equivalent end states with different baselines would bypass duplicate detection.
2. Denormalize the normalized Rank key onto `ProjectGoal` to make a PostgreSQL partial unique index enforce target occupancy. Replace the present all-types index with a non-Rank index and Rank-target index. Migration backfills keys from existing Rank JSON, which cannot contain two in-flight Rank goals per project under today's stricter invariant. Keep friendly API prechecks and translate database races into the same 409.
3. Update every slot mutation path and import grouping, not just `CreateGoalEndpoint`. Project locks serialize distinct-target creates; exact-target races yield one success and one structured conflict. `edit-goal-targets-in-place` later updates the denormalized key in the same transaction as target mutation.
4. Preserve V1 priority order for distinct Rank targets; merge only exact normalized duplicates. A deleted goal is absent, so recreation creates a fresh id. No soft-delete restoration behavior is introduced.

## Risks / Trade-offs

- [Key drifts from JSON target] → Centralize normalization and update it atomically on creation and future target edit; add relational tests.
- [Migration drops the old constraint before backfill] → Backfill under migration transaction and add new indexes before accepting writes.
- [Apps double-count overlapping Rank ranges] → API ships before companion app and both are validated with a two-target fixture; the app change owns interval allocation.

## Migration Plan

Create an EF migration adding the key, backfilling existing Rank memberships, replacing the old partial index with separate Rank/non-Rank indexes. Existing goals, memberships, and priorities remain unchanged. A rollback would restore the old uniqueness only after removing distinct in-flight Rank duplicates, so pre-production rollback requires coordinated data review rather than automatic data loss.

## Open Questions

- Which existing API 409 response shape should carry the optional normalized target without forcing unrelated non-Rank callers to parse it? Keep the required conflict fields; settle serialization detail during endpoint implementation.
