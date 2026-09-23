## Context

`UpdateProjectGoalsEndpoint` already replaces a project's full membership under `ProjectGoalPlanningService.ExecuteLockedMutationAsync`, validates owned goals/slots, rejects orphaning, and normalizes project priority. It lacks a reviewed-set precondition, so a stale whole-list request can overwrite another membership change. `establish-global-goal-priority` will establish a separate canonical order.

## Goals / Non-Goals

**Goals:** Optimistic concurrency for bulk replacement, structured actionable errors, and no global-priority side effect.

**Non-Goals:** A new per-goal mutation endpoint, allowing zero project memberships, or changing status/target.

## Decisions

- Extend the existing `PUT /me/projects/{id}/goals` request with `expectedGoalIds` beside desired `goals` (IDs only). Require it for reviewed bulk saves; V2 is pre-production, so a coordinated contract adjustment can be direct rather than adding a second endpoint.
- Compare expected and current sets *inside* the existing lock before applying additions/removals. Return HTTP 409 with `code: projectMembershipStale` and current IDs for a mismatch. Keep slot 409 distinct; return structured blocked IDs for a last-membership error. No partial writes on any rejection.
- Retain existing EF transaction and project-slot constraint. Do not write the global priority column/order. Coordinate the endpoint's remaining legacy project-priority normalization with `establish-global-goal-priority` before implementation so project membership cannot create a competing execution order.
- Regenerate OpenAPI and update all apps callers in the same paired change. No schema/migration change is required for the precondition itself.

## Risks / Trade-offs

- Requiring the expected set breaks current V2 callers → update the apps companion atomically with the API change; API applies first.
- A global-priority implementation may replace project priority fields → finalize the post-migration projection contract with that change rather than preserving obsolete normalization.

## Open Questions

- Does `establish-global-goal-priority` retire project-local priority storage or keep it only for display compatibility? The membership endpoint must not mutate the canonical order either way; settle before apply.
