## Context

See `proposal.md` — Why. The constraints that shape the approach, all verified
against current source:

- **The rule exists in exactly three places**, each an identical expression:
  `CreateGoalEndpoint.cs:91`, `CreateCombinedGoalsEndpoint.cs:145`, and
  `V1GoalImportService.cs:365`. Nothing else in the codebase derives a goal
  status from membership.
- **`Profile.ActiveProjectId` has four other consumers**, none of them about
  status: it is set by `ActivateProjectEndpoint`, initialized by
  `ProjectsService` (`profile.ActiveProjectId ??= project.Id`, so the first
  project a profile ever gets becomes its active plan), read by `ProjectMapper`
  for the `isActivePlan` marker on project responses, and read by
  `UpdateProjectEndpoint` to refuse archiving the active plan. This change does
  not touch any of them.
- **Membership operations already leave status alone.** `UpdateGoalProjectsEndpoint`
  and `UpdateProjectGoalsEndpoint` read `GoalStatus` only to evaluate slot
  occupancy; neither writes it. `ActivateProjectEndpoint` writes one scalar on
  the profile. So `goal-lifecycle-status`'s "Project operations do not change a
  goal's status" requirement codifies behavior that already holds — it is a
  regression guard, not new work.
- **`Paused` occupies a project goal-type slot.** `project-goal-slots` counts
  `Active` and `Paused` alike, so `startPaused` changes no conflict behavior.
- **The V1 import always files into the default project**
  (`EnsureDefaultProjectAsync`), so under the old rule the whole import was
  `Paused` whenever the user had made some other project current.
- **Both creation endpoints document the old rule in their OpenAPI `Summary`
  descriptions**, so the description text is part of the contract surface that
  changes.

## Goals / Non-Goals

**Goals:**

- Remove membership from the status decision in all three creation paths at once,
  so no path is left contradicting the filter model.
- Give the deliberate "queue this for later" intent an explicit request flag,
  rather than leaving it expressible only as a side effect of which project the
  goal was filed into.
- Keep `Profile.ActiveProjectId`'s remaining roles intact.

**Non-Goals (design-level, beyond the proposal's scope statement):**

- No change to project goal-slot semantics. `Paused` occupies a slot before and
  after.
- No bulk status endpoint. `GP-22`'s bulk pause/resume is Cluster 6 and is not
  pulled in here.
- No removal of `Profile.ActiveProjectId` itself. Whether a profile should have
  one active plan at all is `GP-36`, blocked on `GP-11`.
- No per-goal paused flag inside a combined request.

## Decisions

### A boolean `startPaused` rather than a caller-supplied status

`CreateGoalRequest` and `CreateCombinedGoalsRequest` each gain
`bool StartPaused = false` as a trailing optional positional parameter, matching
how `Snapshot` was already added to `CreateGoalRequest`. Absent means `Active`.

_Why over the alternatives:_

| Option                                         | Verdict                                                                                                                                                                             |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `bool StartPaused = false` (chosen)            | Expresses exactly the two reachable birth states. Additive and defaulted, so existing callers and the generated OpenAPI stay compatible; the behavior change rides on the default.   |
| `string? Status` on the request                | Admits `Completed` and `Archived` at birth, which no path should create — every one of those would need a validator rejecting it, to express one bit.                                |
| No flag at all, always `Active`                | Smaller, but removes the only way to create a goal that is not immediately planned. Rejected by the product owner, who chose the opt-in.                                             |
| Create then `PATCH` the status                 | Two round trips for a first-class intent, and a visible window where the goal is `Active` and enters a plan.                                                                          |

_Naming:_ `startPaused` over `paused` because the flag describes the goal's birth
status only — nothing about it constrains later transitions.

### The flag is request-wide for combined creation

`CreateCombinedGoalsRequest.Goals` is a dependency chain for one unit
(`CombinedGoalSpec.DependsOnIndex` must reference a strictly earlier position). A
chain whose prerequisite is `Paused` while its dependent is `Active` is a state
the planner would have to reason about for no user benefit, so the flag sits on
the request rather than on `CombinedGoalSpec`.

_Alternative considered:_ per-spec flags. Rejected — it invites incoherent chains
and no caller is asking for it. If a real need appears it is additive later.

### The V1 import reads status from the source goal, not from a flag

The import gets no caller-supplied paused option — it is a bulk migration of a
user's existing V1 plan, and the import dialog is `v1-profile-import`'s surface in
the companion repo, not this change's. But it does not create everything `Active`
either: a V1 goal already carries the user's own activation choice, and the import
must preserve it.

V1's `IPersonalGoal.dailyRaids` (`tacticusplanner/src/models/interfaces.ts:389`)
is that choice — `goals.service.ts` maps it straight to `include`, which decides
whether the goal participates in Daily Raids planning. That is the same axis as
V2's `Active`/`Paused`; V2's own start-paused helper text ("created but left out
of daily planning until resumed") describes exactly `dailyRaids: false`. The field
already arrives on the wire (`TacticusV1Client.V1Goal.DailyRaids`) and was read by
nothing, so the import has always silently discarded it — a defect the old
membership-derived rule masked by pausing whole imports anyway.

`TranslatedGoal` therefore carries `InDailyPlanning`, and each created goal's
status is `InDailyPlanning ? Active : Paused`.

_Why over the alternatives:_

| Option                                            | Verdict                                                                                                                                                |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Map `dailyRaids` per goal (chosen)                | Restores the plan the user actually had, goal for goal. The signal is already on the wire and costs one field.                                          |
| Always `Active`                                   | Silently resumes every goal the user had deliberately excluded from daily planning — the same class of defect as the membership rule, in the other direction. |
| Always `Active`, plus a later bulk-pause pass     | Makes the user re-do a choice their V1 data already recorded.                                                                                           |

_Absent flag means `Active`._ `DailyRaids` is widened to `bool?` so a V1 record
written before the field existed imports as `Active` rather than being pushed into
`Paused` by a defaulted `false`. V1's own model has it required, so this only
covers older or partial payloads.

_Synthesized prerequisites take the unit's `Active` choice if any candidate has
it._ A prerequisite exists to unblock that unit's imported goals; pausing one
while an `Active` goal depends on it would leave that goal unworkable. Where every
imported goal for the unit was excluded, its prerequisite is `Paused` too.

_Merged duplicates take `Active` if any duplicate had it._ The survivor stands in
for all of them, so merging must not drop an activation the user had set.

_Consequence, accepted:_ an imported chain can hold an `Active` goal depending on
a `Paused` one when the user's V1 data said so (the dependent was in daily
planning, its prerequisite Ascension was not). That state is reachable in V1 and
V2 imposes no constraint against it; inventing a status the source did not record
would be the larger error.

### No backfill migration

Confirmed with the product owner. A goal that is `Paused` today may have been
paused deliberately or may have been born `Paused` under the old rule; nothing
persisted distinguishes them (`GoalEvent` records a `Created` event, not the
status it was created with). A blanket flip to `Active` would silently resume
goals users chose to pause, and re-pausing is per-goal work. Existing statuses
are therefore left alone and the spec says so explicitly, so the absence of a
migration is a recorded decision rather than an omission.

_Alternative considered:_ backfilling only goals whose `Created` event predates
any status-change event and whose project was never the active plan. The profile
does not retain a history of which project was active when, so this cannot be
reconstructed.

### Companion apps change and ordering

The companion is `clarify-project-purpose` in `tacticus-planner-apps`. The shared
contract surface is the two creation endpoints' request bodies —
`POST /me/goals` and `POST /me/goals/combined` — regenerated into
`artifacts/openapi`.

This API half applies first. The apps half sends `startPaused` and would fail
against an API that does not accept it; the reverse order is safe, because the
API's default reproduces what a client that never sends the field expects. The
behavior change (goals born `Active`) takes effect with this half alone, which is
correct: it is the fix, and the apps half only exposes the opt-out and explains
the model.

## Risks / Trade-offs

- **A user who relied on the old rule to stage goals loses that mechanism
  silently.** Filing a goal into a non-current project no longer parks it. →
  `startPaused` is the replacement and the apps half surfaces it in the creation
  sheet; the behavior it replaces was undocumented and unrequested.
- **A V1 import now activates a whole plan at once for users whose active plan
  was not the default project.** → This is the reported defect, not a new one:
  the import's goals were previously invisible to every plan. Import outcome
  reporting already lists every created goal, and per-goal pause is available.
- **Codifying "project operations do not change a goal's status" adds tests for
  behavior that already holds.** → Deliberate. It is the executable form of the
  `GP-25` constraint, which otherwise exists only in a backlog document.
- **The two endpoints' OpenAPI `Summary` text currently asserts the old rule.**
  A stale description is a contract defect for consumers reading the generated
  artifact. → Updating both descriptions is an explicit task, not incidental.

## Migration Plan

No EF Core migration, no schema change, no data backfill — the only change to
persisted data is which status *new* rows get. No deployment ordering constraint
inside this repo. Rollback is a code revert; goals created `Active` under the new
behavior stay `Active` and remain user-pausable, so a revert leaves no
unreachable state.

Deploy this repo before its `tacticus-planner-apps` companion.

## Open Questions

- Whether the goal detail response should expose *why* a goal is paused (born
  paused vs. paused later). Deferrable: it changes no requirement here, and
  nothing currently asks for the distinction.
