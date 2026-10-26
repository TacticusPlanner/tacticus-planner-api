## Why

The apps change `polish-goal-dialogs` (decision D13) lets the create dialog set each new goal's notes and the batch's priority. Doing that with follow-up edits would be n+1 requests and not atomic, so the creation request itself must carry both.

## What Changes

- `POST /goals`: each `CombinedGoalSpec` takes an optional `notes` (string, nullable), validated exactly like the edit route's `details.notes` (at most 2000 characters, stored as sent; null means no notes).
- `POST /goals`: the request takes an optional `position` (int, nullable), 1 to in-flight count + 1. The created goals take positions `position`, `position + 1`, ... in request order and existing goals at or after `position` shift down. Null appends at the end, as today. Out of range is a 400 validation problem naming `position`, and nothing is created.

## Capabilities

### New Capabilities

- `goal-creation`: what the creation request may set on a new goal beyond its target (notes).

### Modified Capabilities

- `global-goal-priority`: "Lifecycle changes maintain global order" — creation may insert at a requested position instead of always appending.

## Impact

`CreateCombinedGoalsEndpoint` (+ `CreateCombinedGoalsValidator`), `GoalOrderService` (insert at a position), integration tests, regenerated `artifacts/openapi`. No EF migration (`notes` and `globalPriority` columns exist). Companion: `polish-goal-dialogs` in `tacticus-planner-apps`; this API half applies first.
