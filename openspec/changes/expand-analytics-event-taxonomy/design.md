## Context

See proposal.md — Why. The mechanism already exists and is not being rebuilt: `IProductAnalytics` (typed method per event), `PostHogProductAnalytics` / `NullProductAnalytics`, `ProductAnalyticsReporter.TryReport` (fire-and-forget, swallows every failure), and `AnalyticsIdentityDeriver` (pseudonymous id). This change adds five methods and six call sites to that machinery.

The constraints that shape the approach:

- **No background jobs exist in this API.** Every event here has a browser behind it, so "the server sees what no browser can" is not the justification. The justifications are narrower and stated per event in Decisions.
- **`specs/product-analytics-events/spec.md` already sets the privacy floor** — analytics id only, no account id, display name, email, key, guild name, or free text. The new events sit under it unchanged; the bucketing requirement tightens it for quantities.
- **No EF Core migration is required.** Every fact these events need is already loaded in the handler that will emit them.

Companion change: `tacticus-planner-apps` / `expand-analytics-event-taxonomy`, which adds the client-side intent events. **Shared contract surface: none.** This is the unusual cross-repo pair with no endpoint, DTO, or served-dataset overlap — the two halves meet only in the analytics destination, as events attributed to the same analytics id. That is why the API half can merge and deploy on its own without breaking the client.

## Goals / Non-Goals

**Goals:**

- Every new event is derivable from state the emitting handler already has — no new query, no new request field, no new column.
- Failure paths report too. An event set that only fires on success cannot measure friction, which is the point of the change.
- Keep emission uniform: one `TryReport` wrapper, one typed method, one call site per event.

**Non-Goals:**

- **Operational telemetry.** Sync latency, upstream status-code rates, and error volume are OpenTelemetry's job and ServiceDefaults already wires it. These events answer "how many *distinct people* were affected", which OTel cannot.
- **Per-goal or per-entity event volume.** Deliberately excluded; see the per-request decision below.
- **Reporting anything the client can report honestly and cheaply.** Filter changes, tour progress, dialog abandonment, and language switching are all in the companion apps change, not here.
- **A `trigger` request field on player-sync.** Rejected below.

## Decisions

### `is_first_sync` instead of `trigger: onboarding | manual`

The interesting sync question is "did the user ever get data at all", not "which button did they press". A `trigger` property would require the client to send an analytics-only field on `POST tacticus-integration/player-sync` — a contract change made for instrumentation, which the companion client change would then have to send and keep accurate forever.

`is_first_sync` is derived from whether a `PlayerDataSnapshot` already existed for the profile, which the handler establishes anyway. Server-owned, unfalsifiable, no contract change.

*Alternative considered:* a `trigger` header rather than a body field. Same coupling, less visible, harder to keep honest.

### Per-part outcomes on `v1_import_completed`, not one overall outcome

`ImportV1ProfileResponse` already carries an `ImportPartResult` (`Imported` / `Skipped` / `Failed`) per part — Tacticus user id, personal API key, guild token, onslaught progress, campaign event progress, goals. Collapsing six known outcomes into one overall verdict discards exactly the fact that makes the event actionable: *which* part of V1 import is failing. The properties are already computed; reporting them costs nothing.

The per-part *reason* strings (e.g. `not_selected`) are enumerated constants in this codebase, not free text, so they are safe under the privacy floor. `V1ImportIssue` messages are **not** — they can quote V1 content — and are excluded.

### `goals_created` per request with a bucketed count, not per goal

`POST me/goals/combined` creates many goals in one call, and `ImportV1ProfileEndpoint` no longer creates goals itself — it returns `GoalSpecs` that the *client* submits through that same combined endpoint. Emitting per goal would make one V1 import look like a hyperactive power user and would swamp every other event in the dataset.

**Known limitation:** because import-sourced goals arrive through the same endpoint as user-authored ones, `goals_created { source: combined }` conflates the two. Separating them would need an analytics-only request flag — the same contract smell rejected above. At analysis time they are separable by correlating with `v1_import_completed` on the same analytics id within a short window. Recorded here rather than solved in code; if the conflation ever actually blocks a decision, the cheaper fix is a distinct endpoint for import submission, which has independent justification.

### What `goals_created` carries, and what the database keeps

The properties are chosen by one test: **would this dimension be used to segment other analytics, or to decide whether a feature stays?** Everything that passes is low-cardinality and drawn from a vocabulary this system already owns — `GoalType`, `GoalEntityType`, `FarmingStrategy`, `AcquisitionSourceKinds` — so no new enumeration is invented for analytics and none can drift.

The three most valuable are adoption flags for features that were built and have never been measured:

| Property | Feature it evaluates |
| --- | --- |
| `acquisition_sources_selected` + `acquisition_source_kinds` | The multi-select shard acquisition picker, which has a spec of its own. A dominant "left the unrestricted default" result means the picker is complexity nobody asked for. |
| `has_farming_location_override` | The per-goal farming location override (plan §6). |
| `has_dependencies` | Combined-goal dependency chains (plan §8), the most intricate part of combined creation. |

`acquisition_sources_selected` is deliberately three-state — explicitly selected, left at the unrestricted default, or not applicable to this goal kind — because collapsing the last two makes an Unlock goal that declined the picker indistinguishable from a Rank goal that could never have used it, and the whole question is what share of *eligible* goals use it.

**What is excluded, and why:** the targeted unit's identity and every target value (rank span, progression, ability levels, upgrade ids, level). These are not user-authored and not personal data, so the privacy floor does not forbid them — the reason is simpler. Those facts are already in the `goals` table, at full fidelity, queryable retroactively for questions nobody has thought of yet. "Which characters do people plan for" is a `GROUP BY entity_id`. An event property would be a lossy, pre-declared second copy of data already owned, and unlike the aggregate query it would also build a per-account profile of what each person is planning. An event earns its place by carrying what the database *cannot* — timing, sequence, and cohort — not by duplicating what it already holds.

For the same reason the chosen battle ids, shop offer ids, and farming location ids are not reported: the *kind* is the segmentation dimension, and the specific selections are already stored.

### Set-valued properties for combined creation

A combined request targets one entity but may create several goal kinds with several strategies, so `goal_types` and `farming_strategies` are reported as distinct, order-independent sets rather than a single value or a first-wins pick. `entity_type` stays single-valued because combined creation is defined as multiple goal types *for one entity*.

*Alternative considered:* one event per goal so every property could be scalar. Rejected for the same reason the event is per-request at all — a single V1 import would then look like a hyperactive power user and would dominate the dataset.

### Buckets, not exact counts

A raw goal count or roster size is weakly identifying — an account with 4,000 goals is one account. Bucketing costs nothing analytically (every question here is "roughly how big", never "exactly how many") and removes the fingerprint. Bucket edges live in one place beside the analytics feature so they cannot drift between events.

### Enumerated failure reasons, never upstream text

A Tacticus API error body or an exception message can contain a key fragment, a player name, or a guild name. Failure reasons are mapped to a fixed category set (`invalid_key`, `rate_limited`, `upstream_error`, `transform_failed`) at the emission site. Anything unmapped reports as a generic category rather than passing text through.

### Emission stays inside the handler, after the commit

Each event fires in the handler that owns the outcome, after the state change is durable, through `ProductAnalyticsReporter.TryReport`. Not via a decorator, a filter, or a domain-event bus: there are six call sites, and any of those indirections would be more machinery than the thing it dispatches.

*Alternative considered:* an EF `SaveChanges` interceptor deriving events from tracked-entity changes. It would decouple emission from handlers, but it cannot distinguish "activated a project" from "renamed a project", and it makes the privacy floor a runtime property of entity graphs rather than a compile-time property of typed method signatures.

## Risks / Trade-offs

- **Failed syncs are reported for *every* attempt, including a user retrying a bad key ten times** → the event carries `is_first_sync` and an enumerated reason, so retry storms are identifiable as such rather than looking like ten distinct broken users. Not deduplicated server-side; deduplication belongs in the analysis, where the window can be chosen.
- **Six new emission points in hot handlers** → all are fire-and-forget through the existing wrapper, which already guarantees a vendor failure cannot turn a successful request into a 500. The spec requirement "analytics never affects the operation that produced it" covers the new events unchanged.
- **The client half may double-report some of the same moments** → deliberate, and only for onboarding. The delta between the client's `action` count and the server's `tacticus_integration_configured` count is the ad-block/drop rate, which is otherwise unmeasurable. Every other moment is reported on exactly one side.
- **Server events carry no session id** → cross-side funnels (client intent → server outcome) resolve at person level, not session level. Accepted; the abandonment questions this change exists to answer are person-level questions.
- **Bucket edges chosen now will look wrong later** → they are one constant, and changing them re-buckets future events only. Historical events keep their original labels, so a bucket change is a legible break in a chart rather than silent corruption.

## Migration Plan

No schema change, no migration, no OpenAPI change, no client-visible behavior change. Deploys independently and applies before the companion apps change (per this repo's cross-repo ordering). Rollback is a revert: with no analytics destination configured, `NullProductAnalytics` already makes the entire feature inert.

## Open Questions

- Whether `tacticus_api_rejected` deserves to be its own event rather than a `player_sync_completed` failure reason. Deferred: the failure reason already captures it for sync, and a separate event only earns its place if key-validation rejections outside sync turn out to matter. Answering it later changes neither these specs nor the task breakdown.
