# Design

## Context

- Stage 2 (`add-legendary-event-teams`, archived 2026-10-10) created `legendary_event_plans` with one `revision` per plan; every mutation carries `expectedRevision`, touches the plan row so the interceptor bumps `Revision`, and answers with the whole `LegendaryEventPlanResponse`; a stale revision answers 409 `legendaryEventPlanStale` with the current plan. `LegendaryEventPlanWriter` runs each mutation in a transaction under the execution strategy. `LegendaryEventCatalogValidator` validates ids against `IGameCatalogProvider.Current.LreViews`.
- The catalog has no run number; the client takes the current run from the synced `currentEventRun` (Stage 2 design D12). Runs 1–3 share lanes, objectives and missions.
- Raw `lres-*.json` files carry `regularMissions` and `premiumMissions` (10 strings each for every current event), `pointsMilestones` (82 rungs), `chestsMilestones`, `progression` and `shardsPerChest`. `BuildLreCommon` serves the first event's ladder as the single `lre-common` record. Chest ladders differ between events today (60 vs 54 rungs, identical prefix); see the proposal.
- V1 run inputs: `leProgress[event].overview` is `{ "1": {...}, "2": {...}, "3": {...} }` with `regularMissions`, `premiumMissions`, `bundle` (0/1) and optional `ohSoCloseShards`. V1's UI bounds: missions 0–10, close shards 0–75 in steps of 25. `leSettings.showP2POptions` is a profile-wide boolean; V1's default when absent is `true`.
- V1 annotations: `leProgress[event].compactProgress[lane][requirementId].statuses[battleIndex]` holds `RequirementStatus` (0 NotCleared, 1 Cleared, 2 MaybeClear, 3 StopHere, 4 PartiallyCleared); older profiles carry `battlesProgress[{ trackId, battleIndex, requirements[{ id, state, status }] }]`, where `state` 2 (blocked) without a `status` reads as StopHere. Requirement ids are V1 objective display names plus `_killPoints`, `_highScore` and `_defeatAll`.
- The Stage 2 import iterates V1 events from the teams key only and skips the whole event with `plan_already_exists` when the plan already has teams.

## Goals / Non-Goals

**Goals:**

- Store run inputs per run on the plan under the existing revision contract, with no new concurrency rule.
- Serve a reward ladder that is correct for each event, through one canonical structure.
- Let every V1 user bring their run inputs and Maybe clear / Stop here marks, including those who already imported teams.
- Keep Maybe clear / Stop here as reminders on battles not yet cleared, without ever conflicting with synced facts.

**Non-Goals:**

- Computing anything (ADR 0009): no projection, no currency totals on the server.
- Storing synced progress rows (cleared objectives, scores); the client reads them from the synced `lre-progress` chunk.
- Notes or `showPaidOptions` changes beyond the import (the existing `PUT` plan already writes both).

## Decisions

**D1. One row per stored run, keyed `(plan_id, run)`.** `legendary_event_run_progress(plan_id, run, regular_missions, premium_missions, bundle_purchased, close_shards, updated_at)`, PK `(plan_id, run)`, CHECK `run BETWEEN 1 AND 3`, `regular_missions >= 0`, `premium_missions >= 0`, `close_shards >= 0`; FK to the plan with cascade (and so to the profile). Filtered through `e.Plan!.ProfileId`. A run with no row reads as absent; the client treats absent as all zeros. Matches the proposed table in `architecture/data/events.md`.

**D2. Write endpoint `PUT /me/legendary-event-plans/{eventId}/runs/{run}`.** Body `{ expectedRevision, regularMissions, premiumMissions, bundlePurchased, closeShards }`. Upserts that run's row, leaves the other runs untouched, touches the plan, bumps the revision and returns the whole plan; lazy plan creation at `expectedRevision: 0` and the 409 body are exactly as for team writes. A write whose values equal the stored row (or all zero for an absent row) still succeeds but does not bump the revision, like an unchanged reorder, so a client that saves on blur does not create spurious conflicts. There is no delete: zeros are the reset. Rationale: one more mutation on the same writer keeps the client's single conflict path.

**D3. Validation against the event's catalog.** `run` ∈ 1..3 (400 naming `run`; the route value is parsed, not trusted). `regularMissions` ∈ 0..`regularMissions.Count` and `premiumMissions` ∈ 0..`premiumMissions.Count` of the event's served `lres` record (10 each today). `closeShards` ∈ 0..75 (V1's bound; three chest-sized awards). No multiple-of-25 rule: V1 never enforced it and the value is informational for the projection. `bundlePurchased` is a boolean. Paid fields are stored whatever `showPaidOptions` says; hiding them is a client concern.

**D4. Served shape.** `LegendaryEventPlanResponse` gains `runs: LegendaryEventRunProgressResponse[]` ascending by run, absent runs omitted:

```
LegendaryEventRunProgressResponse {
  run: 1 | 2 | 3,
  regularMissions: int,     // 0..event regularMissions count
  premiumMissions: int,     // 0..event premiumMissions count
  bundlePurchased: boolean,
  closeShards: int,         // 0..75
  updatedAt: string         // ISO 8601 UTC, last write of this run
}
```

It also gains `annotations: LegendaryEventObjectiveAnnotationResponse[]`, ordered by lane (`alpha`, `beta`, `gamma`), then `battleIndex`, then `objectiveId`:

```
LegendaryEventObjectiveAnnotationResponse {
  laneId: "alpha" | "beta" | "gamma",
  battleIndex: int,         // 0-based, as synced encounters
  objectiveId: int,         // 0 defeat-all, 1..5 catalog objective index + 1
  status: "maybeClear" | "stopHere",
  updatedAt: string
}
```

Every plan endpoint returns both through the existing `LegendaryEventPlanProjection`; a plan with no rows serves `runs: []` and `annotations: []`.

**D5. Per-event reward ladder on `lres`, `lre-common` removed.** `GameCatalogLreView` gains `Rewards: GameCatalogLreRewards(PointsMilestones, ChestsMilestones, Progression, ShardsPerChest)` copied from the event's own raw file. The `lre-common` dataset, `GameCatalogLreCommon`, `BuildLreCommon` and its route are removed, and the manifest loses the entry. Alternatives considered: (a) keep `lre-common` and add only per-event `chestsMilestones` — two sources for one ladder, against the "one canonical served structure" rule; (b) serve `lre-common` keyed by event — a second dataset whose only key is the `lres` id. Embedding costs about 3 KB per event in the already-small `lres` payload. Validation: `pointsMilestones` strictly ascending by `cumulativePoints` with `milestone` 1..n, `chestsMilestones` with `chestLevel` 1..n, all costs and payouts positive, `shardsPerChest` > 0, every progression step > 0; a failure is a catalog load error like other datasets.

**D6. `MaybeClear` / `StopHere` are kept as objective annotations on uncleared cells (Severyn, 2026-10-10).** V1 lets the user mark a battle's objective "maybe clear" or "stop here, don't attempt"; they are reminders whose computed effect is that the token plan skips a marked token (Stage 4 reads them for the same purpose). V2 stores them as manual rows that sync never touches:

- Table `legendary_event_objective_annotations(plan_id, lane_id, battle_index, objective_id, status, updated_at)`, PK `(plan_id, lane_id, battle_index, objective_id)`, CHECK `lane_id IN ('alpha','beta','gamma')`, `battle_index >= 0`, `objective_id BETWEEN 0 AND 5`, `status IN ('maybeClear','stopHere')`; FK to the plan with cascade; filtered through `e.Plan!.ProfileId`. No row means "not marked".
- Cell ids follow the synced `lre-progress` contract so the client compares them with `objectivesCleared` directly: `battleIndex` is the 0-based `encounters` index, `objectiveId` 0 is defeat-all and 1–5 is the lane objective with catalog `index` `objectiveId − 1`.
- Annotations are plan-wide, not per run: a lane's battle progress carries over between runs.
- **Only uncleared cells.** An annotation applies only while the synced progress shows that objective of that battle not cleared; once sync clears it the annotation is ignored everywhere (not shown, not used by the token plan), so a fully cleared battle has none in effect. The server does not check sync on write (the chunk can be older than the user's view, and a cell is never un-cleared), and does not delete rows sync has cleared: an ignored row costs nothing and a sync never writes the plan, so the plan revision stays stable across syncs.
- Write: `PUT /me/legendary-event-plans/{eventId}/annotations` with `{ expectedRevision, laneId, battleIndex, objectiveIds, status }` sets `status` (`maybeClear`, `stopHere`, or null to clear) on the listed cells of one battle; one cell from the grid, several from a battle-row or token action. Validation: `laneId` ∈ alpha|beta|gamma; `battleIndex` ∈ 0..(lane battle count − 1); `objectiveIds` 1–6 distinct entries, each 0..(the lane's objective count, 5 today); `status` one of the two values or null. Same writer, lazy creation, 409 and unchanged-keeps-revision rule as run inputs.
- The KillScore and HighScore pseudo-objectives V1 keeps per battle have no V2 cell; V1 marks on them map to defeat-all (D7.7).

**D7. V1 import: events from both keys, run inputs and annotations independent of teams.**
1. `V1UserData` gains `LeSettings { ShowP2POptions: bool? }`; `V1LreProgress` gains `Overview: Dictionary<string, V1LreOccurrence?>` (`RegularMissions`, `PremiumMissions`, `Bundle`, `OhSoCloseShards`, all loosely typed numbers), `CompactProgress` (lane → requirement id → `{ States, Statuses }`) and the legacy `BattlesProgress` (`TrackId`, `BattleIndex`, `Requirements[{ Id, State, Status }]`); other fields of those records are not read. The legacy `legendaryEventsProgress` key is read the same way.
2. The events processed are the union of V1 event keys in the teams key and the progress key, in ascending numeric order. The part is `Skipped` with `missing_legendary_event_plans` only when neither the teams key nor the progress key is present. An unreadable progress blob still only drops notes, run inputs and annotations.
3. For each event with an `overview`: each of keys `1`, `2`, `3` with a non-null entry becomes a run row. `regularMissions`/`premiumMissions` clamped to 0..catalog count (issue `run_input_clamped`, value `run:field:original`), `bundle` > 0 → true, `ohSoCloseShards` clamped to 0..75; an entry that is all zero is not written. Other keys are ignored (issue `unknown_run`, value = the key).
4. Teams follow the Stage 2 rules unchanged, including `plan_already_exists` when the V2 plan already has a team. Run inputs and notes are evaluated **separately**: a run already stored in V2 is kept (issue `existing_run_inputs_kept`, value = run); other runs are written. Notes keep their Stage 2 rule (never overwrite non-empty V2 notes), but are now also written to a plan that already has teams when its notes are empty.
5. `showPaidOptions` is set from `leSettings.showP2POptions` only when the import creates the plan and the setting is a boolean; an existing plan's value is never changed. Absent setting → the V2 default (false) stays; V1's own absent-means-true default is not imported.
6. Outcome: `V1LegendaryEventOutcome` gains `RunInputsImported: int` and `AnnotationsImported: int`. Status `Imported` when at least one team, run row, annotation or note was written. Code `imported` when at least one team was written; `inputs_imported` when run inputs, annotations and/or notes were written but no team was, whether the plan already existed or the import created it (a progress-only event); `plan_already_exists` only when nothing at all was written because everything was already in V2. Each event still writes in its own transaction.
7. Annotations: read from `compactProgress` (else the legacy `battlesProgress`). Per lane key (`alpha`/`beta`/`gamma`; another key → issue `unknown_lane`, value = the key) and requirement id, each `statuses[battleIndex]` of 2 becomes `maybeClear` and 3 becomes `stopHere`; in legacy `battlesProgress` a requirement with no `status` and `state` 2 (blocked) becomes `stopHere`, as V1 reads it. Other statuses are ignored (cleared and partial states come from sync). The requirement id maps to a cell: `_defeatAll`, `_killPoints` and `_highScore` → 0; any other id is matched to a lane objective with `V1LegendaryEventObjectiveNames.Matches` → `index + 1`, else issue `unknown_objective` (value `lane:name`, null `teamName`). A battle index outside the lane → issue `unknown_battle` (value `lane:index`). When several V1 requirements land on one cell, `stopHere` wins. A cell already annotated in V2 is kept; the number kept is reported once as `existing_annotations_kept`. Annotations are written whether or not the plan has teams.
The part-level summary rules are unchanged (they key off per-event status).

**D8. Analytics.** None on the API.

## Risks / Trade-offs

- [Removing `lre-common` breaks the shipped run status card until the apps change lands] → the paired apps change switches to `lres[].rewards` in the same release; the apps change is applied right after this one, as with every pair. The catalog version hash changes, so clients refetch.
- [A user edits run 2 in two tabs] → one plan revision serialises them; the losing tab adopts the 409 plan.
- [V1 missions over the current catalog count after a game change] → clamped with an issue, never rejected.
- [`showPaidOptions` not imported for users who imported teams earlier] → accepted; it is one toggle in the run inputs drawer.
- [Annotations on cells sync has since cleared accumulate] → ignored by the client by rule (D6); at most 6 × battles × 3 rows per plan, removed with the plan.

## Migration Plan

One migration `AddLegendaryEventRunInputsAndAnnotations` creating `legendary_event_run_progress` and `legendary_event_objective_annotations` with their PKs, CHECKs and FK cascades; applied on startup. No backfill (no prior data). Rollback drops both tables. The catalog change needs no migration.

## Open Questions

- None. `MaybeClear` / `StopHere` were confirmed as kept, limited to uncleared cells (D6).
