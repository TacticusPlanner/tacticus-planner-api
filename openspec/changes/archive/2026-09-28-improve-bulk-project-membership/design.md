## Context

`UpdateProjectGoalsEndpoint` already replaces a project's full membership under `ProjectGoalPlanningService.ExecuteLockedMutationAsync`, validates owned goals/slots, and rejects orphaning with a plain 400. It lacks a reviewed-set precondition, so a stale whole-list request can overwrite another membership change. `establish-global-goal-priority` has replaced project-local priority with the account-wide `Goal.GlobalPriority`: the request carries goal IDs only, the response lists members in global order with each member's `globalPriority`, and the endpoint writes no order (project-level reordering is the separate `PUT /me/projects/{id}/goal-order` move).

## Goals / Non-Goals

**Goals:** Optimistic concurrency for bulk replacement, structured actionable errors, and no global-priority side effect.

**Non-Goals:** A new per-goal mutation endpoint, allowing zero project memberships, or changing status/target.

## Decisions

- Extend the existing `PUT /me/projects/{id}/goals` request with `expectedGoalIds` beside desired `goals` (IDs only). Require it for reviewed bulk saves; V2 is pre-production, so a coordinated contract adjustment can be direct rather than adding a second endpoint.
- Compare expected and current sets *inside* the existing lock before applying additions/removals. Return HTTP 409 with `code: projectMembershipStale` and current IDs for a mismatch. Keep slot 409 distinct; return structured blocked IDs for a last-membership error. No partial writes on any rejection.
- Retain existing EF transaction and project-slot constraint. Never write `GlobalPriority` or `GoalOrderRevision`: membership replacement does not reorder anything, a goal newly added to a project keeps its existing global position, and a removed goal keeps its position in the account-wide order. The stale-set precondition is independent of the order revision, so a concurrent reorder never makes a membership save stale.
- Regenerate OpenAPI and update all apps callers in the same paired change. No schema/migration change is required for the precondition itself.

## Risks / Trade-offs

- Requiring the expected set breaks current V2 callers → update the apps companion atomically with the API change; API applies first.
## Open Questions

None. Resolved: project-local priority storage is retired (`project_goals.priority` was dropped by `establish-global-goal-priority`); membership replacement never mutates the canonical order.
