## Why

A project is a filter, not a switch: it organizes goals into a named,
independently-ordered view, and per-goal pause/resume is the only activation
mechanism. Three creation paths contradict that — `CreateGoalEndpoint:91`,
`CreateCombinedGoalsEndpoint:145`, and `V1GoalImportService:365` each set
`goal.Status = targetProjects.Any(p => p.Id == profile.ActiveProjectId) ? Active : Paused`,
so filing a goal into a project that is not the Current plan silently creates it
Paused. It is the only place a project visibly *does* something, it is specified
nowhere, and it is the root of the reported confusion about what Projects are
for (`GP-27`) and what membership means (`GP-28`).

The consequence is not only conceptual. A goal created while viewing a
non-current project, or a whole V1 profile imported while some other project is
Current, lands Paused and therefore appears in no daily plan — with nothing in
the response or the UI saying so.

## What Changes

- **BREAKING (behavior, not contract):** a created goal's lifecycle status stops
  being derived from project membership. `POST /me/goals`,
  `POST /me/goals/combined`, and the V1 goal import all stop consulting
  `Profile.ActiveProjectId`, whatever projects a goal is filed into.
- `POST /me/goals` and `POST /me/goals/combined` accept an optional
  `startPaused` flag (default `false`) so a caller can deliberately create a goal
  in the `Paused` status. Omitting it is the existing wire shape, so the contract
  addition is backward compatible even though the default behavior changes.
- The V1 goal import has no such flag, because a V1 goal already records the
  user's own choice: `dailyRaids` (V1's "include this goal in Daily Raids")
  carries it. An explicit `false` imports as `Paused`; `true` imports as
  `Active`, and so does an absent value — a V1 record written before the field
  existed must not be silently paused. The field already arrives on the wire and
  was read by nothing, so today's import discards it.
- `Profile.ActiveProjectId` keeps its remaining roles — the Current-plan marker
  on project responses, the archive guard in `UpdateProjectEndpoint`, and the
  implicit project selection clients fall back to. Only its influence on a new
  goal's status is removed.
- No backfill. Goals already `Paused` under the old rule are indistinguishable
  from goals a user paused deliberately, so existing statuses are left untouched.

## Capabilities

### New Capabilities

- `goal-lifecycle-status`: a goal's lifecycle status is owned by the goal, not
  derived from its project memberships — what status creation produces, how a
  caller opts into `Paused`, and what project membership may and may not change
  about status.

### Modified Capabilities

- `v1-goal-import`: "Imported goals are created by the import operation"
  currently defines the import's status by reference — goals are created "with
  the same in-flight status a goal created through the ordinary create-goal
  operation would receive." Once ordinary creation can produce either status via
  `startPaused`, that reference no longer names one status, and it would
  contradict `goal-lifecycle-status`'s rule that the import always creates
  `Active`. The requirement is restated to name `Active` directly.

<!-- No other api capability states a creation-time status rule:
     `project-goal-slots` governs slot occupancy (Active/Paused alike, so it is
     unaffected) and `goal-target-model` covers targets rather than lifecycle. -->

## Impact

- **Code:** `Features/Goals/CreateGoalEndpoint.cs`,
  `Features/Goals/CreateCombinedGoalsEndpoint.cs`,
  `Features/V1Import/V1GoalImportService.cs`, `Features/V1Import/TacticusV1Client.cs`.
  Request records `CreateGoalRequest` and `CreateCombinedGoalsRequest` each gain
  one optional property; the three status derivations are replaced. `V1Goal.DailyRaids`
  widens to `bool?` and is read for the first time.
- **API contract:** additive request fields regenerate `artifacts/openapi`. Both
  endpoint `Summary` descriptions currently document the membership-derived rule
  and must be corrected.
- **Persistence:** none. No schema change, no EF Core migration, no backfill.
- **Companion apps change:** `clarify-project-purpose` in
  `tacticus-planner-apps` — surfaces the `startPaused` option in goal creation
  and carries the explanatory half of `GP-27`/`GP-28`. This API half applies
  first: the apps half sends the new field.
- **Consumers:** any client relying on "created into a non-current project ⇒
  Paused" changes behavior. Dailies and Insights already scope by selected
  project *and* `Active` status, so a goal created into another project still
  does not enter the Current plan's daily plan — it is simply resumable without
  a second step.
