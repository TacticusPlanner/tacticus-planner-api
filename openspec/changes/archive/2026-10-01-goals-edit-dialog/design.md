## Context

- Companion apps change: `goals-edit-dialog` (this API half applies first).
- Four existing endpoints each own their locking and HTTP writes: `UpdateGoalTargetEndpoint` (revision check, `ProjectGoalPlanningService.ExecuteLockedMutationAsync` with a restart-on-membership-drift loop, Rank slot conflict, `TargetChanged` event), `UpdateGoalEndpoint` (no locks; notes/strategy/locations/acquisition sources), `UpdateGoalProjectsEndpoint` (same lock loop, membership diff, slot conflicts), `UpdateGoalOrderEndpoint` (`GoalOrderService.ReorderAsync` inside a lock with no project ids). Their handler bodies interleave validation, mutation, and `Send.*` calls inside the locked delegate.
- `ExecuteLockedMutationAsync` opens the transaction, takes `SELECT ... FOR UPDATE` on projects in ascending id order under READ COMMITTED, and hands the delegate the transaction; callers commit.

## Goals / Non-Goals

**Goals:**
- One request, one transaction, all-or-nothing, reusing the existing rules and error bodies.
- No behavior change to the four existing endpoints.

**Non-Goals:**
- No new persistence, migration, or event types beyond what the sections already write.
- No client-supplied id list for priority (the server derives the order from a position).
- Not replacing the existing endpoints (the project/goal reorder UIs and other callers keep using them).

## Decisions

1. **One endpoint, optional sections, `PUT me/goals/{goalId}/edit`.** Absent section = unchanged, which avoids the null-ambiguity the target endpoint's summary calls out (null clearing vs leaving). Alternative: a `PATCH` with merge semantics, rejected for the same ambiguity; alternative: a batch/transaction endpoint taking a list of operations, rejected as broader than needed.
2. **Extract, don't duplicate.** Each existing handler's mutation core moves into a transaction-scoped method on a service (`GoalTargetEditor.ApplyAsync`, `GoalDetailsEditor.ApplyAsync`, `GoalMembershipEditor.ApplyAsync`, `GoalOrderService.MoveToPositionAsync`) that takes already-loaded, already-locked state, mutates the tracked entities and returns a result union (`Applied`, `Invalid(section, message)`, `StaleRevision(current)`, `SlotConflict(body)`, `OrderConflict(body)`) instead of writing the response. The existing endpoints become thin wrappers that call the same methods and map the result to the same HTTP responses, so their behavior is preserved by construction and by their existing tests. The new endpoint calls the four in a fixed order inside one locked delegate.
3. **Order of application inside the transaction:** target, then details, then projects, then priority. Target first so the project slot check (Rank slot key derives from the target) sees the new target and so the `expectedRevision` check runs against the state the client loaded; projects before priority because a membership change never affects the global order but a failed membership must abort before order is written. One `SaveChangesAsync` at the end (with the existing `DbUpdateConcurrencyException` -> stale and slot-constraint -> conflict handling), then one commit. Any non-`Applied` result rolls the transaction back and sends the mapped 400/409.
4. **Locks.** Lock set = the goal's memberships before the edit union the requested `projectIds` (as `UpdateGoalProjectsEndpoint` does), with the same reload-under-lock and restart-on-drift loop. A priority-only or details-only request with no projects section still locks the goal's current memberships when a target section is present, and `[]` otherwise (as the order endpoint does today), so cheap edits don't take project locks unnecessarily.
5. **Priority by position.** `MoveToPositionAsync` reads the in-flight order under the lock, validates 1..N and that the goal holds a position, and applies the same array move as `moveOntoDisplaced` (remove and reinsert at index position-1). Requiring `expectedOrderRevision` detects a client that computed the position from an older order; a mismatch returns the existing `goalOrderStale` body with the current order.
6. **Revision.** `expectedRevision` (goal revision) is required with `target` and ignored otherwise, matching today's semantics (notes/projects edits are last-write-wins today). The goal row is touched once, so the request bumps the revision once (the target path already forces `UpdatedAt` modified).
7. **Response.** `EditGoalResponse { goal: GoalDetailResponse, order?: GoalOrderResponse }`. Alternative: return only the goal and make the client refetch the order; rejected because the client already has an order query to update.

## Risks / Trade-offs

- [Refactoring four handlers risks regressions] -> the extraction keeps each endpoint's HTTP mapping tests untouched and adds no logic; run the existing endpoint tests before adding the new endpoint's.
- [Deadlock from a widened lock set] -> reuse `ExecuteLockedMutationAsync`'s ascending-id locking; add a concurrency test with two overlapping edits.
- [Response writes inside the delegate are already fragile with the execution strategy (see the comment in `ExecuteLockedMutationAsync`)] -> the new endpoint records a result inside the delegate and writes the response once after it returns.
- [A no-op detail (unchanged target) inside a combined edit] -> treated as applied-without-change, not an error, so an otherwise-valid edit still succeeds.

## Migration Plan

Additive endpoint; regenerate the OpenAPI artifact and the apps client types. No data migration. Rollback: remove the endpoint; the apps half is reverted with it.
