## Context

`Profile.ActiveProjectId` (Current plan in the UI, `isActivePlan` in the DTO) is a nullable, FK-less pointer created in `InitialCreate`. Its only remaining effects are: `ActivateProjectEndpoint` sets it; `ProjectMapper.ToSummary` derives `IsActivePlan`; `UpdateProjectEndpoint` returns 409 `activeProjectCannotBeArchived` for it; `GetCurrentUserEndpoint` seeds it to the Default project on provisioning; `ProjectsService.EnsureDefaultProjectAsync` does `ActiveProjectId ??= project.Id`; and `AddGlobalGoalPriority` read it once to order the backfill. Goal creation, import and planning paths never consult it. The Default project is a separate concept: `Project.Type = Default`, named "My Goals", not archivable (`defaultProjectCannotBeArchived`), and the last-membership fallback. There is no project delete endpoint.

The paired apps change `consolidate-goals-into-plan-and-remove-active-project` removes the client half (Make current, badges, current-plan ordering and defaults) and also folds Global Plan into Goals and renames routes; those are apps-only.

## Goals / Non-Goals

**Goals:** Remove the active-project pointer end to end (column, endpoint, DTO field, guard, seeding); make "one Default project per profile" a database invariant.

**Non-Goals:** Delete or forbid custom projects; add a project delete endpoint; localize the Default project's name; change goal ordering, membership rules, statuses or `ProjectStatus` (Active/Paused/Archived is unrelated); change any Dailies/Insights recommendation policy.

## Decisions

1. **Clean removal, no shim.** V2 is pre-production (destructive-changes policy), so delete `ActivateProjectEndpoint`, `IsActivePlan`, the archive guard and the column in one change. `ToSummary` loses its `activeProjectId` parameter and list/create/update endpoints stop loading the profile when nothing else needs it.
2. **Migration after `AddGlobalGoalPriority`.** That migration consumes `active_project_id` for the former-Current-plan-first backfill, so the new migration must sort later. It (a) inserts a Default project ("My Goals", type Default, status Active) for every profile without one, (b) collapses any profile that somehow has several Default rows to the oldest by `created_at, id` — demoting the rest to Custom, never deleting, so memberships are untouched, (c) creates a unique partial index `(profile_id) WHERE type = 'Default'`, and (d) drops `profiles.active_project_id`. `Down` re-adds the nullable column pointing at each profile's Default project. Users whose active project was non-default lose only that browse preference; goals, memberships and order are untouched.
3. **Index over app code for "exactly one Default".** Lazy creation in `EnsureDefaultProjectAsync` is racy today; the partial unique index makes it safe, and the service treats a unique violation as "another request created it" and re-reads. It is added to the EF model (unlike the deferred goal-order constraint, it never has to permute values) via `HasIndex(...).IsUnique().HasFilter(...)`.
4. **Keep the archive guard for Default only.** `defaultProjectCannotBeArchived` stays; `activeProjectCannotBeArchived` is deleted. Pausing the Default project and emptying it stay allowed (a goal's only remaining membership cannot be removed, so goals always retain a membership), so no new rule is introduced.
5. **Default project stays renamable/editable.** Unchanged behaviour, now stated in the new `default-project` capability along with the filing-fallback rule already implemented in create/import/last-membership paths.
6. **Provisioning.** `GetCurrentUserEndpoint` still creates the Default project for a new account but no longer sets the pointer; `EnsureDefaultProjectAsync` remains as a defensive fallback for the rare profile without one.

## Risks / Trade-offs

- [Apps still reading `isActivePlan` or calling `activate` break] → Apply API first only together with the apps release; the apps change stops using both before or with this rollout. A field missing from a response is the only interim symptom.
- [Duplicate Default rows block the unique index] → The migration collapses duplicates first (decision 2) and has a fixture test for it.
- [Migration runs automatically on startup] → Small data change; take the standard backup already documented in `RELEASING.md`, prefer forward repair.
- [Ordering] → `establish-global-goal-priority` must be archived first; its "Current plan remains a browsing preference" line is superseded, its migration history remains true.

## Migration Plan

Apply `establish-global-goal-priority` first, then this API change, then the paired apps release. The EF migration backfills Default projects, enforces uniqueness and drops the column in one transaction; verify every profile has exactly one Default project and unchanged goal/membership counts. If the rollout fails, prefer forward repair; `Down` restores an approximate column but not users' former selections.
