## Context

See proposal.md — Why. See specs/v1-goal-import for the behavior contract.

Current shape, and where each defect lives:

```
 V1 API              tacticus-planner-api                    apps/web
 ------              --------------------                    --------
 34 goals --> V1Goal DTO
                 |  drops shardFarmType, campaignsUsage,
                 |  mythicCampaignsUsage            <-- MIG-03
                 v
            V1GoalImportService.TranslateAsync
                 |  OrderBy(Priority)               <-- order known HERE
                 |  Translate  (already-reached checks
                 |    default to bottom of ladder
                 |    when no snapshot)             <-- fabricated starts
                 |  CollapseProgressionGoals
                 |    (Rank/Ascension merges emit
                 |     no issue at all)             <-- invisible merges
                 |  dedupe vs db.Goals
                 |  GroupBy(entity)  34 goals -> 19 specs
                 v
            GoalSpecs[] (Snapshot null) ----------> ImportV1Dialog
                                                        |  resolves snapshots
                                                        |  Promise.allSettled
                                                        v  19 PARALLEL POSTs
                                              POST me/goals/combined x19
```

Target shape:

```
 V1 API              tacticus-planner-api                    apps/web
 ------              --------------------                    --------
 34 goals --> V1Goal DTO  (+ shard-source fields)
                 |
                 v
            V1GoalImportService.ImportAsync
                 |  no player data -> refuse goals part, return
                 |  OrderBy(Priority) -> translate -> collapse -> dedupe
                 |  synthesize prerequisites per unit
                 |  build snapshots from the loaded player data
                 |  ExecuteLockedMutationAsync(default project):
                 |     per candidate, in V1 order:
                 |        validate        -> failed outcome, continue
                 |        slot conflict?  -> skipped outcome, continue
                 |        else stage Goal + membership(base + i)
                 |     SaveChanges  (once)
                 |     NormalizeAsync  (once)
                 |     SaveChanges; Commit
                 v
            GoalOutcomes[]  (one per source goal, + synthesized) --> dialog
                                                                     renders
                                                                     buckets
```

## Goals / Non-Goals

**Goals:**

- One request creates the goals, in V1 priority order, with a per-source-goal
  outcome.
- No silent loss: every dropped field, merged duplicate, and refused goal is
  reported.
- Prerequisite behavior identical to the manual create-goal flow.
- Partial success survives: a rejected goal does not discard its siblings.

**Non-Goals:**

- A preview-then-confirm step. The import commits on submit, as today. This is
  what makes folding creation into the existing operation viable at all; if a
  preview is wanted later it needs a second operation and a decision about
  trusting client-supplied specs.
- Extracting a shared goal-creation service. The combined-creation endpoint is
  fail-fast and welded to HTTP error emission; the import needs
  continue-on-error with structured outcomes. Those are different behaviors,
  not a missing abstraction.
- Preserving V1's absolute priority numbers or its interleaving of different
  units' goals. Both are unrepresentable against unit-block ordering.
- Preserving V1 timestamps. V1 stores none — not in its goal model, not in its
  reducer, not in the wire DTO. There is nothing to carry.
- Letting the user choose a target project. Imported goals go to the default
  project, as today.
- Reporting every target error from combined creation instead of the first.
  That endpoint keeps its fail-fast behavior; the import does not use it.

## Decisions

### Fold creation into `POST me/v1-import` rather than adding a second operation

The two-step design's stated reason is that the server cannot build a goal's
initial-state snapshot. It can. Every field that builder needs — the unit's
rank, its progression index, its ability levels — is already read out of the
same player data snapshot the translator loads, and the one remaining input
(the missing-upgrade set feeding the initial requirement) is passed as empty by
the import path. The client's values are a serialized copy of the server's, one
hop staler, arriving via a string round-trip.

So the round trip carries no information the server lacks. Removing it deletes
the returned specs, the client's snapshot resolution, the parallel fan-out, and
the units-versus-goals counting confusion in one move.

A separate `POST me/v1-import/goals` was considered and rejected: V1
credentials are used once and never persisted, so a second operation would
need either a second V1 login or to accept the translated specs back from the
client — a new trust boundary and an extra round trip, for no gain absent a
preview step.

### Refuse the goals part without player data, rather than defaulting

With no player data snapshot, every resolver degrades to the bottom of the
ladder: current rank becomes the first rank, current progression becomes the
lowest, ability levels become zero, and the roster lookup returns nothing. The
consequence is not a failed import but a *successful* one carrying invented
starting points — inflated remaining-cost estimates throughout, plus Unlock
goals for characters the player already owns, with nothing on screen to
indicate it.

This is the normal onboarding path: the import installs the personal API key
in the same request that translates the goals, and the first sync happens
later. Refusing is the only option that does not silently corrupt the plan.
Note the validator's defaulting runs the other way — it treats a missing
snapshot as permitting the maximum — so nothing downstream catches the bad
start either.

*Alternative considered:* import, then recompute starting points after the
first sync. Rejected for this change: it needs a backfill path that does not
exist, and a goal's start is a recorded baseline rather than a derived value,
so rewriting it later is a data migration, not a refresh.

### One locked mutation for the whole batch; per-goal decisions are reads

Every per-goal rejection is decided by a read — target validation and slot
conflict detection both query without writing — so a rejected goal never
dirties the transaction. "One by one" and "partial success survives" are
therefore not in tension: the loop makes per-goal decisions, stages only the
survivors, and commits once.

This is also the point of the design: `NormalizeAsync` renumbers every
membership row in the project, so running it once per batch rather than once
per goal is what keeps the batch from fighting itself. All-or-nothing *writes*
cost nothing here because the import is idempotent by construction — the dedupe
pass drops candidates whose unit-and-type slot is already taken, so re-running
after a total failure creates no duplicates.

Ordering falls out without extra machinery: candidates are already in V1
priority order, memberships get consecutive priorities in that order, and
`NormalizeAsync` orders unit blocks by each block's minimum priority.

*Alternative considered:* a savepoint per goal, so a write failure isolates to
one goal. Rejected — after the read-phase checks, a write failure is genuinely
exceptional, and savepoints would add real complexity for it.

### Do not extract a shared creation service

The import calls the same collaborators the combined endpoint calls — target
validation, conflict detection, the locked-mutation and normalize helpers, the
default-project service, and the config mapper. What it does not reuse is the
endpoint handler itself, which is fail-fast and writes HTTP errors directly.

The genuinely duplicated code is the goal construction plus one membership
call, and even that differs: the import sets notes, builds the snapshot from a
domain value rather than parsing a wire value, and resolves its own dependency
edges. Extraction is the right move when a third caller appears, not the
second.

### Port the prerequisite tables into `ProgressionRules`, duplicated from the client

Prerequisite detection needs three static tables the server lacks:
rank-to-minimum-progression, rank-to-required-character-level, and the inverse
of the existing ability-cap table. Roughly forty lines of constants, no catalog
data.

They are duplicated from the client's equivalents rather than shared through
codegen or a generated contract — which is the convention the two already
follow deliberately for the progression ladder itself. These are a few dozen
numbers that change when the game changes; a sharing mechanism would cost more
than it saves, and the client tests for them already exist to mirror.

### Synthesis is a fixed, shallow pass — no recursive resolver

The prerequisite graph is acyclic and two deep:

```
              Unlock          (unit absent from roster)
                |
        +-------+-------+
        v               v
    Ascension        Level    (characters only)
        |   \          /  |
        v    +--> Ability   Upgrade <- [Unlock]
      Rank
```

Nothing above Unlock has a synthesizable prerequisite, and Unlock has none. So
synthesis is a straight-line three-step sequence per unit — Unlock, then
Ascension, then Level, then the imported goals — with no fixpoint loop, no
visited set, and no cycle detection. Emitting them in that order also satisfies
the combined-creation constraint that a dependency reference point at a
strictly earlier entry.

The synthesized Ascension target is the *minimum* that satisfies every
requirement for that unit. Anything more generous would invent game-design
policy the user did not ask for, and it is what the manual flow does.

### Report an existing lower prerequisite target; never raise it

When the unit's own imported goals already include an Ascension goal whose
target is below what another imported goal needs, the import reports the
shortfall and leaves the target alone. Raising it would satisfy the
one-goal-per-unit-and-type invariant and clear the blocker, but it would
silently overwrite a target the user explicitly chose in V1. Reporting matches
the manual flow, which suggests a prerequisite only when the user has not
chosen that goal type themselves.

### Check live state before consulting existing goals

The account-level dedupe pass has no status filter, so a *completed* Ascension
goal for a unit would otherwise block synthesis and report as
already-exists. Evaluating the requirement against live player data first
avoids that: if the progression requirement is already met, there is nothing to
synthesize and nothing to report, whatever historical goals exist.

## Risks / Trade-offs

- **Refusing the goals part makes the onboarding path a two-step flow** —
  import the key, sync, import the goals. → Unavoidable given the alternative
  is a corrupted plan, and the refusal message says exactly what to do. The
  companion apps change surfaces it as a blocking outcome, not an error.
- **A long V1 profile now runs inside one transaction holding the project row
  lock.** → Bounded by V1's own hundred-goal limit, and the work per goal is
  small primary-key reads plus one insert. Target validation re-reads the
  player data snapshot per goal, which is redundant inside the loop; worth a
  marked shortcut with the ceiling named rather than a premature optimisation.
- **Synthesis creates goals the user did not author.** → Gated on a selection
  flag, reported as its own outcome entry identified as automatically added,
  and identical to what the manual flow already does on every create. Default
  on, matching the manual flow's defaults.
- **Prerequisite synthesis does not make an imported goal render unblocked.**
  → It changes the reason from a *missing* prerequisite to an *unreached* one.
  That is the same state a manually created goal reaches, which is the parity
  being asked for. Softening the blocked indicator for a correctly sequenced
  plan is a separate presentational question, deliberately not decided here.
- **The response is a breaking change to the import contract.** → V2 is
  pre-production; the companion apps change lands in the same cycle and is the
  only consumer.
- **The API test host runs on EF Core InMemory, where the locked mutation
  no-ops.** → Outcome, ordering and classification are all testable there;
  the transactional behavior is not. Concurrency coverage belongs to
  `fix-goal-mutation-isolation-level`; this change adds a PostgreSQL-backed
  test only for batch ordering and contiguity.
- **Ability targets need the depended-upon Ascension to lift their cap.** →
  Supplied by `fix-goal-ability-cap-effective-progression`. Without it,
  synthesized Ascension plus above-cap Ability is rejected, so that change
  must land first.

## Migration Plan

No schema change and no data migration; the import response shape changes and
the OpenAPI artifact regenerates on build.

Apply order: `fix-goal-mutation-isolation-level`, then
`fix-goal-ability-cap-effective-progression`, then this change, then its
`tacticus-planner-apps` companion. Rollback is reverting the endpoint and
service together with the companion client change — the two response shapes
are not compatible, so they deploy and roll back as a pair.
