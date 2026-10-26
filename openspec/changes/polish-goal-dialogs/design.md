## Context

`POST /goals` (`CreateCombinedGoalsEndpoint`) appends every created goal through `GoalOrderService.AppendAsync` inside `ProjectGoalPlanningService.ExecuteLockedMutationAsync`. The edit route validates notes with `MaximumLength(GoalValidation.MaxNotesLength)` and stores them as sent, and reports an out-of-range `priority.position` as a 400 validation failure. Companion: `polish-goal-dialogs` in `tacticus-planner-apps` (its D13).

## Goals / Non-Goals

**Goals:** notes per spec; one insert position per request.

**Non-Goals:** a position per spec; an expected order revision on create (append never had one, and an insert at a stale position is harmless: the client reads the new order back).

## Decisions

- **Notes rule reused, not re-stated:** each spec's notes get `MaximumLength(GoalValidation.MaxNotesLength)`, the same rule as `UpdateGoalValidator`. No trimming, because edit does not trim either.
- **Position range checked under the lock:** the validator only rejects `position < 1`; the upper bound needs the in-flight count, so the handler checks it after reading `InFlightAsync` under the profile lock and sends a 400 validation failure on `position`, mirroring the edit route's `Priority.Position` failure. Both bounds use the same message.
- **Insert = append, then move:** the goals append as today, then `GoalOrderService.MoveAppendedToAsync` moves the appended block to `position` in the in-memory order and renumbers. The order stays dense, with the single revision bump in `CompleteAsync`. The positions' unique constraint is deferred, so the permutation is safe in one transaction.

## Risks / Trade-offs

- [The client computes `position` from a stale count] → an out-of-range value is a plain 400; the client refetches. Acceptable for a pre-production contract.
