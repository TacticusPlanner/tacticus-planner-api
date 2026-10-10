# Design

## Context

- Stage 2 (`add-legendary-event-teams`, archived 2026-10-10) created `legendary_event_plans` with one `revision` per plan; every mutation carries `expectedRevision`, touches the plan row so the interceptor bumps `Revision`, and answers with the whole `LegendaryEventPlanResponse`; a stale revision answers 409 `legendaryEventPlanStale` with the current plan. `LegendaryEventPlanWriter` runs each mutation in a transaction under the execution strategy. `LegendaryEventCatalogValidator` validates ids against `IGameCatalogProvider.Current.LreViews`.
- The catalog has no run number; the client takes the current run from the synced `currentEventRun` (Stage 2 design D12). Runs 1–3 share lanes, objectives and missions.
- Raw `lres-*.json` files carry `regularMissions` and `premiumMissions` (10 strings each for every current event), `pointsMilestones` (82 rungs), `chestsMilestones`, `progression` and `shardsPerChest`. `BuildLreCommon` serves the first event's ladder as the single `lre-common` record. Chest ladders differ between events today (60 vs 54 rungs, identical prefix); see the proposal.
- V1 run inputs: `leProgress[event].overview` is `{ "1": {...}, "2": {...}, "3": {...} }` with `regularMissions`, `premiumMissions`, `bundle` (0/1) and optional `ohSoCloseShards`. V1's UI bounds: missions 0–10, close shards 0–75 in steps of 25. `leSettings.showP2POptions` is a profile-wide boolean; V1's default when absent is `true`.
- The Stage 2 import iterates V1 events from the teams key only and skips the whole event with `plan_already_exists` when the plan already has teams.

## Goals / Non-Goals

**Goals:**

- Store run inputs per run on the plan under the existing revision contract, with no new concurrency rule.
- Serve a reward ladder that is correct for each event, through one canonical structure.
- Let every V1 user bring their run inputs, including those who already imported teams.

**Non-Goals:**

- Computing anything (ADR 0009): no projection, no currency totals on the server.
- Storing per-battle annotations (`maybe`, `stop`) or synced progress rows (D6).
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

Every plan endpoint returns it through the existing `LegendaryEventPlanProjection`; a plan with no rows serves `runs: []`.

**D5. Per-event reward ladder on `lres`, `lre-common` removed.** `GameCatalogLreView` gains `Rewards: GameCatalogLreRewards(PointsMilestones, ChestsMilestones, Progression, ShardsPerChest)` copied from the event's own raw file. The `lre-common` dataset, `GameCatalogLreCommon`, `BuildLreCommon` and its route are removed, and the manifest loses the entry. Alternatives considered: (a) keep `lre-common` and add only per-event `chestsMilestones` — two sources for one ladder, against the "one canonical served structure" rule; (b) serve `lre-common` keyed by event — a second dataset whose only key is the `lres` id. Embedding costs about 3 KB per event in the already-small `lres` payload. Validation: `pointsMilestones` strictly ascending by `cumulativePoints` with `milestone` 1..n, `chestsMilestones` with `chestLevel` 1..n, all costs and payouts positive, `shardsPerChest` > 0, every progression step > 0; a failure is a catalog load error like other datasets.

**D6. `MaybeClear` / `StopHere` are not carried into V2 (default, flagged for the product owner).** They are per-battle, per-objective reminders in V1 ("maybe clear", "stop here, don't attempt") whose only computed effect is that V1's tokenomics skips a flagged token when picking the first one to show. In V2, per-run team clear depth (Stage 2, estimated in Stage 5) is the "stop here" signal, and synced progress replaces cleared states. Keeping them would add a manual per-battle table that sync must never touch, a UI on the grid, and an import path, for the lowest-value data in the V1 import research. So: no `legendary_event_objective_progress` table, and the import keeps ignoring `compactProgress.statuses`. If Stage 4's token plan needs a "skip this token" control, it is designed there as a token-plan action, not a grid annotation. The docs feature spec's open question is answered by this decision once accepted.

**D7. V1 import: events from both keys, run inputs independent of teams.**
1. `V1UserData` gains `LeSettings { ShowP2POptions: bool? }`; `V1LreProgress` gains `Overview: Dictionary<string, V1LreOccurrence?>` (`RegularMissions`, `PremiumMissions`, `Bundle`, `OhSoCloseShards`, all loosely typed numbers). The legacy `legendaryEventsProgress` key is read the same way.
2. The events processed are the union of V1 event keys in the teams key and the progress key, in ascending numeric order. The part is `Skipped` with `missing_legendary_event_plans` only when neither the teams key nor the progress key is present. An unreadable progress blob still only drops notes and run inputs.
3. For each event with an `overview`: each of keys `1`, `2`, `3` with a non-null entry becomes a run row. `regularMissions`/`premiumMissions` clamped to 0..catalog count (issue `run_input_clamped`, value `run:field:original`), `bundle` > 0 → true, `ohSoCloseShards` clamped to 0..75; an entry that is all zero is not written. Other keys are ignored (issue `unknown_run`, value = the key).
4. Teams follow the Stage 2 rules unchanged, including `plan_already_exists` when the V2 plan already has a team. Run inputs and notes are evaluated **separately**: a run already stored in V2 is kept (issue `existing_run_inputs_kept`, value = run); other runs are written. Notes keep their Stage 2 rule (never overwrite non-empty V2 notes), but are now also written to a plan that already has teams when its notes are empty.
5. `showPaidOptions` is set from `leSettings.showP2POptions` only when the import creates the plan and the setting is a boolean; an existing plan's value is never changed. Absent setting → the V2 default (false) stays; V1's own absent-means-true default is not imported.
6. Outcome: `V1LegendaryEventOutcome` gains `RunInputsImported: int`. Status `Imported` when at least one team, run row or note was written. Code `imported` when at least one team was written; `inputs_imported` when run inputs and/or notes were written but no team was, whether the plan already existed or the import created it (a progress-only event); `plan_already_exists` only when nothing at all was written because everything was already in V2. Each event still writes in its own transaction.
The part-level summary rules are unchanged (they key off per-event status).

**D8. Analytics.** None on the API.

## Risks / Trade-offs

- [Removing `lre-common` breaks the shipped run status card until the apps change lands] → the paired apps change switches to `lres[].rewards` in the same release; the apps change is applied right after this one, as with every pair. The catalog version hash changes, so clients refetch.
- [A user edits run 2 in two tabs] → one plan revision serialises them; the losing tab adopts the 409 plan.
- [V1 missions over the current catalog count after a game change] → clamped with an issue, never rejected.
- [`showPaidOptions` not imported for users who imported teams earlier] → accepted; it is one toggle in the run inputs drawer.
- [Dropping `MaybeClear` / `StopHere` loses a V1 habit] → flagged to the product owner; adding a table later is additive.

## Migration Plan

One migration `AddLegendaryEventRunProgress` creating `legendary_event_run_progress` with its PK, CHECKs and FK cascade; applied on startup. No backfill (no prior data). Rollback drops the table. The catalog change needs no migration.

## Open Questions

- Should V2 keep `MaybeClear` / `StopHere` at all? Default here: no (D6). Needs the product owner's confirmation before apply.
