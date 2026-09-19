## Context

See proposal.md — Why.

`GoalTargetValidationService.ValidateAsync` is called once per spec, with the
profile, entity type, entity id, goal type and that spec's config. It loads
the account's player data snapshot itself and derives the ability-level cap
from `ProgressionRules.AbilityCapForProgression(playerUnit?.ProgressionIndex)`.
Nothing in its signature can express "and this request also ascends the unit
first", so the cap is always the live one.

`CreateCombinedGoalsEndpoint` validates every spec in a loop before it does
anything else, and returns on the first error. It therefore turns one
above-cap ability target into a rejection of the entire unit's goal set. That
loop already parses each spec's `GoalType` and already holds the whole
`req.Goals` list plus each spec's `DependsOnIndex`, so it has everything
needed to compute the effective progression — it simply does not.

Two shapes exercise this today:

```
  manual create-goal flow (shipped, broken):
    client detects an above-cap ability target
      -> auto-suggests an Ascension prerequisite
      -> submits [Ascension, Ability] as one combined request
      -> server validates Ability against LIVE progression -> 400
      -> whole request rejected, no goals created

  V1 import (rewrite-v1-goal-import):
    same shape, once per unit, at scale
```

The client-side spec builder wires `Ability <- [Unlock, Level]` and omits an
`Ability <- Ascension` edge, even though the ability target is one of the two
things that can trigger the Ascension suggestion. That omission is why the
dependency-gated design below needs a matching client change.

## Goals / Non-Goals

**Goals:**

- An ability target reachable only after an Ascension goal in the same request
  is accepted, provided the dependency is declared.
- Single-goal creation and update behavior is bit-for-bit unchanged.
- The rule is expressed once, so any future progression-derived cap benefits.

**Non-Goals:**

- Changing combined creation's fail-fast behavior. Reporting every target
  error instead of the first is a separate concern; the V1 import gets
  per-goal outcomes from its own change, and the manual flow submits a handful
  of specs where fail-fast is acceptable.
- Introducing cross-request awareness. Only Ascension specs inside the *same*
  request count; an Ascension goal that already exists on the account does not
  raise the cap, because it has not completed and the ability target is not yet
  reachable.
- Validating that the depended-upon Ascension is itself achievable. That is
  already its own spec's validation.
- Any schema or persistence change.

## Decisions

### Pass an effective progression into validation rather than letting validation discover it

`ValidateAsync` gains an optional effective-progression parameter. The caller
computes it; the service uses it in place of the live progression when
deriving a progression-based cap, and falls back to the live value when the
caller passes nothing.

Rationale: the validation service is per-spec by design and has no view of
sibling specs. Teaching it to re-read the request would invert the dependency
and make single-goal validation carry combined-request concepts. Keeping the
computation in the endpoint that already owns the request keeps the service a
pure function of "this goal, this unit, this progression".

*Alternative considered:* validate the whole combined request in one call,
giving the service the full spec list. Rejected — it would duplicate the
endpoint's parsing and dependency resolution inside the validator and make the
single-goal path a special case of the combined one for no benefit.

### Gate the lift on a declared dependency, not on mere presence in the request

Only Ascension specs that the goal being validated declares a dependency on
count toward its effective progression.

Rationale: presence-based lifting would accept an ability target on the
strength of an Ascension goal that carries no ordering relationship to it, so
the plan could legitimately schedule the ability work first and the target
would be unreachable when it ran. The dependency edge is the thing that makes
the ordering real — `OrderGoals` places dependencies before dependents — so
tying the cap to the same edge keeps validation and scheduling consistent.

This is what forces the companion client change: the current spec builder does
not emit the `Ability -> Ascension` edge, so under this rule the shipped flow
would still be rejected. The edge is added on the client as part of the apps
half of `rewrite-v1-goal-import`.

*Alternative considered:* lift the cap from any Ascension spec in the request.
Simpler and would fix the shipped flow with no client change, but it accepts
plans whose ordering does not guarantee reachability. Rejected as the wrong
trade: a silent accept of an unreachable target is worse than requiring the
edge that was meant to be there anyway.

### Take the maximum over the transitive dependency set

A spec's effective progression considers the Ascension targets of every spec
in its transitive dependency closure within the request, not only direct
edges, and takes the highest. The prerequisite graph is a fixed, shallow DAG
(`Unlock -> {Ascension, Level} -> {Rank, Ability, Upgrade}`), and combined
requests already guarantee strictly-earlier dependency indices, so the closure
is computable in one forward pass with no cycle handling.

## Risks / Trade-offs

- **The shipped manual flow stays broken until the client edge lands.** → The
  client change is named in the proposal and carried in the apps half of
  `rewrite-v1-goal-import`. If that slips, this change's Non-Goal boundary
  makes the presence-based fallback a one-line escape hatch — but taking it
  would discard the reachability guarantee, so it needs a deliberate decision,
  not a quiet patch.
- **Accepting an ability target that depends on an uncompleted Ascension means
  the goal is not actionable yet.** → Correct and intended: it renders as
  blocked on its prerequisite, which is exactly how the manual flow already
  behaves for rank goals behind an ascension.
- **A future progression-derived cap could be added without routing through
  the effective progression.** → Mitigated by deriving every such cap from a
  single parameter on the service rather than from the snapshot at each use
  site, so the next cap has one obvious place to read from.
- **No player data recorded means the effective progression comes only from
  the request.** → Deliberate, and narrower than today's behavior, which
  defaults the cap to the top of the ladder when the snapshot is missing and
  so accepts anything. The V1 import blocks goal import entirely without a
  snapshot (see `rewrite-v1-goal-import`), and the manual flow cannot be
  reached without synced data.

## Migration Plan

No schema change and no data migration. Behavioral only; effective on deploy.
Rollback is reverting the validation signature and its two call sites.
