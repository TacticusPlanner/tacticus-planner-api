## Why

The API reports exactly two product events today — `account_registered` and `guild_registered`. Both answer "did someone arrive?"; neither answers "did they get anywhere?". Every moment between arriving and successfully planning something is unmeasured: whether the blocking onboarding gate is passed, whether a V1 profile imports cleanly, whether player data actually syncs, whether goals and projects are ever created.

Two of those moments cannot be trusted to the browser at all. The audience is mobile gamers, so a meaningful share of `posthog-js` payloads never leave the device; the conversion that matters most (passing the onboarding gate) is exactly the one that must survive an ad blocker. The rest need the database to distinguish a first occurrence from a repeat, which the browser cannot do.

This is the backend half of a cross-repo pair. The companion `tacticus-planner-apps` change `expand-analytics-event-taxonomy` adds the client-side intent events that these outcome events pair with, and applies second.

## What Changes

- Extend `IProductAnalytics` with five new typed event methods, keeping the existing one-method-per-event shape that makes the privacy floor a compile-time property rather than a review convention.
- **`tacticus_integration_configured`** `{ via: api_key | v1_import, first_time }` — emitted when a profile gains a usable Tacticus API key. The ad-block-proof leg of the onboarding funnel.
- **`v1_import_completed`** `{ tacticus_user_id, personal_api_key, guild_token, onslaught_progress, campaign_event_progress, goals }`, each an outcome enum (`imported` / `skipped` / `failed`), plus `goals_parsed_bucket` and `goals_skipped_bucket` — per-part outcomes, because "which part of V1 import fails" is the actionable question and a single overall outcome discards it.
- **`player_sync_completed`** `{ outcome, failure_reason, is_first_sync }` — the data spine every planning surface reads from. `is_first_sync` is derived from whether a snapshot already existed, so it is a fact the server owns rather than a client-supplied hint.
- **`goals_created`** — emitted once per successful create request, not once per goal, carrying:
  - **volume**: `source` (`single` / `combined`) and `count_bucket`;
  - **shape**, from this system's own closed vocabularies: `entity_type`, the distinct set of `goal_types`, the distinct set of `farming_strategies`, `project_assignment` (explicit vs default-project fallback), and `created_status` (entered the active plan vs parked);
  - **optional-feature adoption**: `acquisition_sources_selected` (explicit selection vs the unrestricted default vs not applicable), the distinct `acquisition_source_kinds` when selected, `has_farming_location_override`, and `has_dependencies`. These three features — the acquisition-source picker, the per-goal farming location override, and combined-goal dependency chains — are real build cost with no usage signal today.

  It deliberately carries **no unit identity and no target values**. Which characters people plan for, and to what ranks, is a `GROUP BY` away in the goals table with full fidelity and retroactively; an event property would be a lossy second copy that also profiles an individual account's plans.
- **`project_activated`** — whether the project concept is adopted at all, or is a feature two people use.
- Numeric properties are reported as **buckets, never raw counts**, so a roster or goal-list size cannot be used to single out an account.
- No new endpoint, no contract change, no schema change, no migration. Every event is emitted from an existing handler through the existing fire-and-forget `ProductAnalyticsReporter`.

### Decisions already taken

| Decision | Choice | Rationale |
| --- | --- | --- |
| Event granularity | One typed method per event on `IProductAnalytics` | Established by the existing change. A free-form property-bag overload would move the privacy floor from the compiler back into code review. |
| Sync trigger | `is_first_sync` (server-derived), **not** `trigger: onboarding \| manual` | The server cannot know what prompted a sync without the client telling it, and an analytics-only request field is a contract change made for the wrong reason. First-vs-subsequent is the distinction that carries the activation signal anyway. |
| Goal events | Per-request `goals_created`, not per-goal | `POST me/goals/combined` creates many goals in one call; per-goal emission would make a single import look like a power user. |
| Numeric properties | Bucketed | `specs/product-analytics-events/spec.md` already forbids identifying values; a raw goal count is weakly identifying and buckets cost nothing analytically. |
| Failure detail | Enumerated reason categories | A raw exception message or upstream response body can contain a key fragment or a player name. |

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `product-analytics-events`: adds requirements for the five new reported events and their bucketing rule. The existing `account_registered` / `guild_registered` requirements, the privacy floor, the "analytics never affects the operation that produced it" guarantee, and the inert-when-unconfigured guarantee are unchanged — the new events sit underneath them.

## Impact

- `src/TacticusPlanner.Api/Features/Analytics/IProductAnalytics.cs`, `PostHogProductAnalytics.cs`, `NullProductAnalytics.cs`: five new methods.
- Emission call sites, all existing handlers: `TacticusIntegration/UpdateTacticusIntegrationEndpoint.cs`, `V1Import/ImportV1ProfileEndpoint.cs`, `PlayerData/PlayerSyncEndpoint.cs`, `Goals/CreateGoalEndpoint.cs`, `Goals/CreateCombinedGoalsEndpoint.cs`, `Projects/ActivateProjectEndpoint.cs`.
- `tests/TacticusPlanner.Api.Tests/`: `RecordingProductAnalytics` gains the new methods; `ProductAnalyticsTests` gains per-event coverage including the negative cases (failed sync, rejected key, re-activation).
- **No schema change, no migration, no OpenAPI change.** Nothing observable to the client changes, which is why this half can merge and deploy independently of the apps half.
- `PlayerSyncEndpoint` already records `TacticusSyncLastAttemptedAt`; `is_first_sync` is derived from the snapshot's prior existence in the same handler, adding no query.
- Import-sourced goals arrive through `POST me/goals/combined` like user-authored ones, so `goals_created { source: combined }` conflates the two. Separable at analysis time by correlating with `v1_import_completed` on the same analytics id — recorded in `design.md` rather than solved with a request flag.
