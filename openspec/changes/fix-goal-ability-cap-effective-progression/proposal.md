## Why

Ability goal targets are validated against an ability-level cap derived from
the unit's **live** progression. Combined goal creation validates each spec in
isolation, so it cannot see that the same request also contains an Ascension
goal that raises the unit's progression — and therefore the cap — before the
ability target is ever farmed.

This makes a sequence the product already promises impossible. The
`goal-creation` capability in `tacticus-planner-apps` requires that an
above-cap ability target auto-suggest an Ascension prerequisite, and the
client duly submits Ascension-then-Ability as one combined request. The server
rejects it. Worse, combined creation returns on the **first** target error, so
one above-cap ability target discards every other goal for that unit in the
same request.

This blocks the V1 goal import (`rewrite-v1-goal-import`), which imports
exactly this shape at scale, and it is independently a live defect in the
manual create-goal flow.

## What Changes

- Target validation for a goal inside a combined request resolves the unit's
  **effective progression**: the higher of the unit's live progression and the
  highest Ascension target among the specs that goal depends on within the same
  request.
- The ability-level cap is derived from that effective progression, so an
  ability target that is reachable only after an accompanying Ascension goal is
  accepted.
- Single-goal creation and goal updates are unchanged: with no accompanying
  Ascension spec, the effective progression is the live progression, which is
  today's behavior.
- A dependency edge is required for the lift. An ability spec that does not
  declare a dependency on the Ascension spec in the same request does **not**
  get the raised cap, so the lift cannot be obtained by accident.
- The client-side prerequisite wiring gains the missing `Ability -> Ascension`
  dependency edge so the promised sequence actually declares its dependency.
  **BREAKING** for nothing on the wire; it changes which edges the client
  sends.

## Capabilities

### New Capabilities

<!-- none -->

### Modified Capabilities

- `goal-target-model`: adds requirements covering how a progression-derived
  target cap is resolved inside a combined request, and that the lift is
  gated on a declared dependency. The existing per-kind and entity/goal-type
  validation requirements are unchanged.

## Impact

- `src/TacticusPlanner.Api/Features/Goals/GoalTargetValidationService.cs` —
  the progression-derived cap becomes a function of an effective progression
  passed in by the caller rather than of live player data alone.
- `src/TacticusPlanner.Api/Features/Goals/CreateCombinedGoalsEndpoint.cs` —
  computes the effective progression per spec from the request's Ascension
  targets and the spec's declared dependencies, and passes it into validation.
- `src/TacticusPlanner.Api/Features/Goals/CreateGoalEndpoint.cs` and the goal
  update path — pass the live progression, preserving current behavior.
- Regenerated `artifacts/openapi` artifact: no schema change expected, to be
  confirmed on build.
- Companion `tacticus-planner-apps` work: the missing `Ability -> Ascension`
  prerequisite edge in the combined-spec builder. Small enough to carry in the
  apps half of `rewrite-v1-goal-import` rather than its own paired change;
  named here so the dependency is not lost.
- `rewrite-v1-goal-import` (both repos) depends on this change. Apply this
  first.
