# Proposal

## Why

Stage 2 of the V2 Legendary Event plan ("Teams and plan persistence", docs plan §5, [ADR 0008](https://github.com/TacticusPlanner/tacticus-planner-docs/blob/main/decisions/adr/0008-lre-owned-teams.md)) is the first stage with persisted state. Stage 1 shipped the hub, the synced progress grid and the eligibility leaderboard from catalog and synced data alone; every later stage (run inputs, tokenomics, clear-depth estimation, goals integration, history) reads from the plan and teams this change introduces. The Wave 1 survey's fourth-ranked LRE pain point is the V1 team builder, and 34% of LRE users reuse teams across events, so persisted, lane-scoped teams with a cross-event copy path are the next useful thing to ship.

The API currently has no Legendary Event planning state at all: `PlayerDataOverride` explicitly excludes LRE annotations and the V1 import client ignores `leTeams`.

## What Changes

- **Persisted plans and teams.** New tables `legendary_event_plans` (one per profile and event, revisioned, pinned to the catalog version it was last written against, with `notes` and `show_paid_options`), `legendary_event_teams` (lane, name, dense sort order per lane), `legendary_event_team_members` (position, unit id, reserve flag), `legendary_event_team_objectives` (objective index) and `legendary_event_team_run_depths` (clear depth with its source per run 1–3, so a run-2 depth never overwrites run 1 and Stage 6 history can read earlier runs). Migration included; applied on startup like every other migration.
- **Plan endpoints under `/me/legendary-event-plans/{eventId}`.** `GET` returns the plan (an empty revision-0 plan when none exists); `PUT` writes the plan-level fields; `POST …/teams`, `PUT …/teams/{teamId}`, `DELETE …/teams/{teamId}` and `PUT …/teams/order` mutate teams. Every mutation carries `expectedRevision`, bumps the plan revision, and answers with the whole plan; a stale revision answers 409 with the current plan, mirroring the goal-target conflict contract.
- **Catalog validation.** Lane ids, unit ids (against the lane's `availableUnitIds`), objective indexes (against the lane's `unitsRestrictions`), run (1–3) and clear depth (against the lane's battle count) are validated against the current catalog on every write. Validation failures are 400s naming the offending field.
- **V1 import part `legendaryEventPlans`** on `POST /me/v1-import`: reads V1 `leTeams` (falling back to `legendaryEvents3`) and `leProgress[*].notes` (falling back to `legendaryEventsProgress`), resolves events, lanes, units and objectives by the rules in the docs research note [LRE V1 Data Import](https://github.com/TacticusPlanner/tacticus-planner-docs/blob/main/research/current-app-analysis/lre-v1-import.md), creates plans and teams, and reports one outcome per V1 event with stable codes.
- **OpenAPI artifact** regenerated under `artifacts/openapi`.

Out of scope (later stages): run inputs (`legendary_event_run_progress`, Stage 3), the plan list endpoint `GET /me/legendary-event-plans` (Stage 7), server-side estimates or suggestions (never; ADR 0009), finished events in the catalog, importing V1 progress annotations.

## Capabilities

### New Capabilities

- `legendary-event-plans`: the persisted plan and teams per profile and Legendary Event, their validation against the catalog, the revision contract and the served shape.
- `v1-legendary-event-import`: the `legendaryEventPlans` part of the V1 profile import: what is read, how events, lanes, units and objectives are resolved, and the per-event outcome report.

### Modified Capabilities

- None. The base V1 import endpoint has no main spec of its own in this repo; the new part is specified in `v1-legendary-event-import` and the existing `v1-goal-import` is untouched.

## Impact

- Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-teams`; apply API first. The shared contract is the `LegendaryEventPlanResponse` shape and the 409 body described in the `legendary-event-plans` spec; the apps change hand-writes its DTOs from it.
- `src/TacticusPlanner.Domain/LegendaryEvents/*` (new entities and Vogen ids), `src/TacticusPlanner.Persistence` (configurations, `PlannerDbContext` sets and profile query filters, `VogenEfCoreConverters`, one migration), `src/TacticusPlanner.Api/Features/LegendaryEventPlans/*` (new feature slice), `src/TacticusPlanner.Api/Features/V1Import/*` (new part), `src/TacticusPlanner.GameCatalog` (a lookup helper for LRE lanes and the V1 numeric event id), `artifacts/openapi/TacticusPlanner.Api.json`.
- Tests: `tests/TacticusPlanner.Api.Tests` (endpoint, validation, revision and import tests on InMemory), `tests/TacticusPlanner.Persistence.IntegrationTests` (migration, cascade and concurrency on Postgres).
- No infra change. No catalog dataset change.
- Docs: `architecture/data/events.md` describes objectives by a text `objective_id` and an `events` schema; this change uses the catalog's integer objective `index` and the repo's single `public` schema (see design D1 and D3). The docs page should be amended when the change is archived.
