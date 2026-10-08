# Design

## Context

- The API has no persisted Legendary Event state. Catalog `lres` views give each event three lanes keyed `alpha|beta|gamma`, each with `unitsRestrictions` (objectives identified only by their 0-based `index`), `availableUnitIds`, and `battleIds`. The synced `lre-progress` chunk refers to objectives by that same `index`.
- Every existing table lives in the default `public` schema under `UseSnakeCaseNamingConvention()`. No migration has ever created a schema.
- There is no catalog release id. `GameCatalogRelease.Version` is a build constant; `SourceHash` changes per process start and cannot pin anything. All existing validation (goals) checks against `IGameCatalogProvider.Current`.
- Revision handling: `IRevisionedEntity` plus `EntityMetadataInterceptor` bumps `Revision` on every modified root. Child-row changes do not mark the root modified unless the code touches it. Goals use entity revisions with `expectedRevision` in the request and a 409 body that carries the current entity (`GoalRevisionConflictResponse`); the goal order uses a profile-level order revision and a deferred unique constraint kept out of the EF model because EF cannot permute a unique position index.
- `/me/v1-import` applies independently selected parts and reports `ImportPartResult(Status, Code, Message)` per part, with per-item outcomes only for goals. `V1UserData` ignores unknown JSON properties, so new keys are added without touching the existing parts.
- FastEndpoints; route values via `Route<T>()`, validators per request, catalog and DB checks in the handler, profile scoping by the DbContext global query filter.

## Goals / Non-Goals

**Goals:**

- One plan per profile and event, teams per lane, members with an optional reserve, covered objectives, manual clear depth, dense lane order; all validated against the catalog on write.
- A single, simple concurrency contract the client can implement once: one revision per plan, every write echoes the plan.
- V1 teams and notes importable in one part, with per-event outcomes the UI can bucket.
- Cascade-safe with account purge; tenant-filtered like every profile-owned entity.

**Non-Goals:**

- Run inputs, token forecasts, clear-depth estimation, suggested teams, plan listing across events, history. Nothing is calculated server-side (ADR 0009).
- A shared `planning.teams` surface (ADR 0008).
- Introducing PostgreSQL schemas; see D1.

## Decisions

**D1. Tables live in `public` with a `legendary_event_` prefix, not in an `events` schema.** The docs data model places these tables in an `events` schema per ADR 0001, but the repo has no schema anywhere: every table, every migration, the integration tests' raw SQL and `ProjectGoalPlanningService`'s SQL all assume `public`. Introducing the first schema inside a feature change would mix a repo-wide convention switch into Stage 2. The prefix keeps the bounded context readable; moving to schemas is a separate change if wanted. The docs page is amended at archive time.

**D2. Catalog pin is `catalog_version` = `GameCatalogRelease.Version`, validation is against the current catalog.** The plan row stores the catalog version it was last written under and every response echoes it as `catalogVersion`. There is no second catalog to validate against, so every write validates against `IGameCatalogProvider.Current`; a plan written under an older version whose event or objectives no longer validate is reported on the next write (400), not silently rewritten. Reads never fail on a catalog mismatch: the client re-derives coverage from the ids it is given.

**D3. Objectives are identified by lane objective `index`.** The catalog has no objective string id; `index` is the identity the synced progress chunk uses and the client's `objectivesSatisfied` returns. `legendary_event_team_objectives` is `(team_id, objective_index)`. Lanes are the catalog track keys `alpha|beta|gamma` stored as text with a CHECK constraint.

**D4. One revision per plan; every mutation is a plan write.** `legendary_event_plans.revision` is the concurrency token. Team create, update, delete and reorder each take `expectedRevision`, run inside one transaction, touch the plan row (`UpdatedAt` marked modified so the interceptor bumps `Revision`) and return the full `LegendaryEventPlanResponse`. A stale `expectedRevision`, or a `DbUpdateConcurrencyException` at save, returns 409 `LegendaryEventPlanConflictResponse { issueCode: "legendaryEventPlanStale", message, plan }` with the current plan, so the client reloads from the body like it does for goal targets. A plan is created lazily: `GET` of a missing plan returns an empty plan with `revision: 0`, and any mutation with `expectedRevision: 0` creates the row. Rationale: teams are small and always edited in the context of one event page; one revision avoids per-team revisions plus a per-lane order revision, and the full-plan response removes the need for client-side merging.

**D5. Dense 0-based `sort_order` per lane, no unique index.** `PUT …/teams/order` takes `{ expectedRevision, laneId, teamIds }` with the complete id set of that lane and answers 409 `legendaryEventOrderSetMismatch` (same body shape, current plan attached) when the set differs or has duplicates. Create appends at `max + 1`; delete re-densifies the lane. Uniqueness is not enforced by an index because EF cannot permute a unique position column without the deferred-constraint workaround used for goals, and the plan-level revision already serialises writers.

**D6. Members: `position` 0–4 plus a `reserve` flag.** `legendary_event_team_members(team_id, position, unit_id, reserve)`, PK `(team_id, reserve, position)`, unique `(team_id, unit_id)`, CHECK `reserve = FALSE AND position BETWEEN 0 AND 4 OR reserve = TRUE AND position = 0`. A team has 1–5 non-reserve members and at most one reserve. Requests carry `memberUnitIds: string[]` (ordered, 1–5) and `reserveUnitId: string | null`; the server assigns positions from the array order.

**D7. Validation per write, 400 with field names.** `eventId` must be a current `lres` id (404 on `GET`/write for an unknown event, so a stale route is distinguishable from a bad body). `laneId` ∈ lane keys. Every member and reserve unit id ∈ `lane.availableUnitIds` and no unit repeated. Every `objectiveIndexes` entry ∈ the lane's `unitsRestrictions[].index` and no index repeated. `expectedBattleClears` null or 1..`lane.battleIds.Count`, and when non-null `expectedBattleClearsSource` ∈ `estimate|manual` (null when depth is null). `name` trimmed, 1–60 characters. `notes` ≤ 2000 characters. Lane changes on `PUT …/teams/{teamId}` are rejected (a team belongs to its lane; the client deletes and recreates).

**D8. Served shape (canonical, one projection for all endpoints):**

```
LegendaryEventPlanResponse {
  eventId: string, revision: long, catalogVersion: string,
  notes: string | null, showPaidOptions: boolean,
  teams: LegendaryEventTeamResponse[]   // ordered by laneId then sortOrder
}
LegendaryEventTeamResponse {
  id: guid, laneId: "alpha"|"beta"|"gamma", name: string, sortOrder: int,
  memberUnitIds: string[],              // positions 0..4 in order
  reserveUnitId: string | null,
  objectiveIndexes: int[],              // ascending
  expectedBattleClears: int | null,
  expectedBattleClearsSource: "estimate" | "manual" | null
}
```

Requests: `PUT plan { expectedRevision, notes, showPaidOptions }`; `POST team { expectedRevision, laneId, name, memberUnitIds, reserveUnitId, objectiveIndexes, expectedBattleClears, expectedBattleClearsSource }`; `PUT team/{teamId}` the same minus `laneId`; `DELETE team/{teamId}?expectedRevision=`; `PUT teams/order { expectedRevision, laneId, teamIds }`.

**D9. V1 import part.** New `V1UserData` fields `LeTeams`, `LegendaryEvents3`, `LeProgress`, `LegendaryEventsProgress` as loosely typed records (unknown properties ignored), read into `V1LegendaryEventImportData(IsPresent, IReadOnlyList<V1LegendaryEventSource>)`. Resolution, per event, in `V1LegendaryEventImportService`:
1. Event: V1 numeric id → catalog raw `GameCatalogLre.Id` → served `lres` id; absent → outcome `event_not_in_catalog`, event skipped.
2. Teams: `teams` when non-empty; otherwise synthesised from the legacy `alpha|beta|gamma` maps as V1's `populateTeams` does (one team per restriction name, merging identical unit sets within a lane).
3. Lane: `section` as-is; unknown → `unknown_lane`, team skipped.
4. Unit: `charSnowprintIds` ?? `charactersIds` ?? `characters[].snowprintId`, then the nine V1 `canonicalName` aliases, then catalog characters and the lane's `availableUnitIds`. Unknown → unit dropped, code `unknown_unit`; not allowed → dropped, `unit_not_allowed_on_lane`; a team left empty → `empty_team`, skipped. More than five → the first five kept, `team_truncated`.
5. Objective: each `restrictionsIds` entry matched against every lane objective's regenerated V1 display name (`objectiveDisplayName` rules ported: `Trait`/`NotTrait` → trait name / `No <trait>`, `MinHits`/`MaxHits` → `Min|Max <n> hits`, `AttackType` → `Ranged`/`Melee`, `DamageType`/`Faction`/`Alliance` likewise, overrides `Terminator`/`No Terminator`) and against the catalog name, case-insensitively after whitespace normalisation and the aliases `Resiliant→Resilient`, `No Range→No Ranged`. Unresolved → dropped, `unknown_objective`.
6. Depth: `expectedBattleClears` → `expected_battle_clears` with source `manual`, clamped to the lane's battle count; 0 or negative → null.
7. Duplicates: within one lane, teams with identical member sets are merged (objectives unioned, first name kept), code `duplicate_team_merged`.
8. Target: a plan that already has any team → whole event skipped, `plan_already_exists`; otherwise plan created (or an empty one reused), `notes` set from `leProgress[event].notes` when present, teams appended in V1 order.
Outcomes: `V1LegendaryEventOutcome(EventId, V1EventId, Status "Imported"|"Skipped"|"Failed", Code, Message, TeamsImported, IReadOnlyList<V1LegendaryEventIssue>)`, where an issue is `(Code, TeamName, Value)` for every dropped unit, objective or team. The part result is `Imported` when at least one event imported, `Skipped` with `missing_legendary_event_plans` when neither key is present, `Failed` with `invalid_legendary_event_plans` when the blob cannot be read. Each event is written in its own transaction so one failure does not discard the others.

**D10. Tenant scoping and cascades.** `LegendaryEventPlan` gets `HasQueryFilter(e => e.ProfileId == CurrentProfileId)`; teams, members and objectives are filtered through their parents (`e.Plan!.ProfileId`, `e.Team!.Plan!.ProfileId`). FKs cascade plan → team → member/objective and profile → plan so `DELETE /me` keeps working.

**D11. Analytics.** No new product-analytics events on the API; the apps change emits team created/edited/deleted and depth overridden client-side per the feature spec.

## Risks / Trade-offs

- [One revision per plan means two tabs editing different lanes conflict] → accepted; the 409 carries the current plan so the losing tab reloads and retries with one tap. Teams are edited rarely and by one person.
- [No unique index on `sort_order`] → the service re-densifies on every reorder and delete, and the plan revision serialises writers; an integration test asserts density after a permutation.
- [Catalog drift: an objective index gains a different meaning when the catalog changes an event] → `catalogVersion` is echoed so the client can warn; index stability within an event is a catalog-maintenance rule noted in the game-catalog-data skill.
- [V1 objective name resolution misses a legacy spelling] → it drops that objective with `unknown_objective` and the value; coverage is re-derived client-side anyway, so the team is still useful.
- [InMemory tests cannot exercise CHECK constraints or concurrency] → Postgres integration tests cover the migration, the constraints, cascade on purge and a concurrent-write 409.

## Migration Plan

One migration `AddLegendaryEventPlans` creating the four tables with their constraints and indexes (`ix_legendary_event_plans_profile_id_event_id` unique, `ix_legendary_event_teams_plan_id_lane_id`), applied on startup. No backfill: there is no prior data. Rollback is dropping the four tables.

## Open Questions

- None blocking. Whether `show_paid_options` belongs on the plan or on user settings was decided by the docs data model (plan); Stage 3 reads it.
