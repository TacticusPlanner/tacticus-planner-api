## Context

After `rebuild-foundation-pre-launch`, slots are account-scoped. `ProjectGoalPlanningService.FindConflict` compares `(EntityType, EntityId, GoalType, RankTargetKey.For(...))` under the profile lock. The key is non-null only for Rank, so every other type collapses to one slot per unit. There is no DB constraint, so the rule can change without a migration.

`CreateCombinedGoalsEndpoint` rejects two specs of one goal type ("Each goal type may appear at most once"). It resolves `DependsOnIndex` against earlier specs only, and lifts the cap-validation progression floor from Ascension specs in the spec's dependency closure.

`GoalEditing` re-checks the slot only when `RankTargetKey` changes. `V1GoalImportService.CollapseDuplicates` groups by `GoalKey(entityType, entityId, goalType, rankKey)`, spans Ascension duplicates and keeps the first of other types.

V1 itself had no uniqueness at all (only a 20-goal cap).

Companion apps change: `support-multiple-goals-per-unit`.

## Goals / Non-Goals

**Goals:** V1 parity for multiple goals per unit for every type except Unlock; exact-duplicate protection; ladders creatable atomically; new milestones can depend on existing goals; lossless V1 import of distinct targets.

**Non-Goals:** multiple Unlock goals; a bulk multi-unit creation endpoint (the client loops combined requests per unit); server-side need allocation (the client owns it, as for Rank); a per-goal goal cap like V1's 20.

## Decisions

1. **One `GoalTargetKey.For(goalType, config)` for every type**, generalizing `RankTargetKey`. It holds the end state only; start/baseline, farming strategy and sources are never part of it, so equal end states conflict however they were reached.
   - Rank: unchanged (`"<end>:<slots>"`, `RankTargetKey.From`).
   - Ascension: `"<endProgressionIndex>"`.
   - Ability (Character and Mow): `"<active>:<passive>"`, where a track whose `end <= start` (not advanced) is written `-`, so a goal that leaves a track alone does not differ by the level that track happened to be at.
   - Upgrade: the targets sorted by upgrade id as `"<id>x<qty>"` joined by `,`, then `|r<start>-<end>`, `|a<start>-<end>`, `|p<start>-<end>` for each present range group. Only an identical material set, quantities and ranges is a duplicate.
   - Unlock: `null`. A null key means one slot per unit and type, which preserves today's behavior for Unlock.
   `RankTargetKey` stays as the Rank implementation detail, and `SameSlot` compares `GoalTargetKey.For`.
   - *Alternative:* compare whole configs. Rejected, same reason as `rank-milestones`: equivalent end states would slip past duplicate detection.
   - *Alternative:* drop uniqueness entirely like V1. Rejected: exact duplicates are always user error and double the plan.
2. **Combined requests allow repeated goal types.** Remove the `requestGoalTypes` guard. `FindConflict` already checks candidates against each other (`claimed`), so two same-key specs in one request still return `slotOccupied`, naming the earlier spec. Specs of one type keep request order. The client is responsible for chaining them with `DependsOnIndex`.
3. **`CombinedGoalSpec.DependsOnGoalIds: List<Guid>?`** (optional, default empty). Each id must name an in-flight (Active/Paused) goal of the same profile and the same entity, read under the profile lock. Otherwise the request is rejected with 400. The resolved `Goal.DependsOn` is the union of in-request and existing ids. For cap validation, `effectiveProgressionFloor` is the max end of Ascension goals in the spec's in-request closure *and* of existing Ascension goals among `DependsOnGoalIds`, plus their own existing `DependsOn` (one level is enough, because only Ascension lifts the floor). A new goal can't be a dependency of an existing one, so no cycle can form.
   - *Alternative:* let the client edit the existing goal's `dependsOn` afterwards. Rejected: that's two writes, it isn't atomic, and the new goal would fail cap validation before the edge exists.
4. **Edits re-check the slot when `GoalTargetKey` changes,** for every keyed type, in the same lock and transaction.
5. **`slotOccupied.normalizedTarget`** is `GoalTargetKey.For(existing)` (null only for Unlock). The message text uses "with this target" whenever a key exists.
6. **V1 import** groups by `GoalKey(entityType, entityId, goalType, GoalTargetKey)`. Distinct keys survive in V1 priority order with their own notes. For exact duplicates, Rank and Ascension keep their span merge (lowest start, union of sources), and other types keep the highest-priority source. Prerequisite synthesis (Unlock/Ascension for above-cap Rank/Ability) first looks for an existing in-flight or same-import Ascension goal whose end covers the need, and depends on it instead of creating a new one. It creates one only when none covers.

## Risks / Trade-offs

- [Client double-counts overlapping Ascension/Ability ranges before the apps half ships] → ship API and apps back to back (README "Breaking contracts"). Until then the client still blocks same-type creation via its own slot check, so nothing new can be created.
- [Upgrade key is strict: same materials with a different quantity are distinct] → intended. Quantities are the target, and the client's coverage allocation prevents double counting of overlapping rank/ability slots.
- [More goals per unit grow the order list] → no cap added. V1 capped at 20 total; V2 has no cap today.
- [`DependsOnGoalIds` referencing a goal completed concurrently] → the ids are read under the profile lock, so a goal that left in-flight before the lock is rejected with 400.

## Migration Plan

No schema change and no data migration. Existing data already satisfies the stricter old rule, so it satisfies the new one. Rollback: re-adding the old rule would make units with two in-flight goals of one type un-resumable, which needs a data review before rolling back pre-launch.

## Open Questions

- Should Upgrade identity ignore quantity (so changing quantity is an edit, not a second goal)? This proposal says no, for exactness; revisit if users create near-duplicates.
