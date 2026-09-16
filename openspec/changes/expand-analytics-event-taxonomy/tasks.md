## 1. Analytics surface

- [ ] 1.1 Add the five typed methods to `IProductAnalytics` (`TacticusIntegrationConfigured`, `V1ImportCompleted`, `PlayerSyncCompleted`, `GoalsCreated`, `ProjectActivated`) with XML doc comments naming the moment each one reports, and verify `dotnet build` succeeds once implementations exist.
- [ ] 1.2 Add the enumerated property types the methods take — the integration path, the sync failure category, the goal creation source, and the V1 import part outcome — as enums or closed constant sets rather than `string` parameters, and verify no call site can pass an arbitrary value.
- [ ] 1.3 Add the quantity bucketing helper beside the analytics feature with a single fixed edge set shared by every event, and verify unit tests cover the zero, boundary, and above-highest-edge cases.
- [ ] 1.4 Implement the five methods in `PostHogProductAnalytics` (property names snake_case, matching the spec's event names) and as no-ops in `NullProductAnalytics`, and verify the existing "analytics is inert when not configured" test still passes.

## 2. Emission call sites

- [ ] 2.1 Emit `tacticus_integration_configured` from `UpdateTacticusIntegrationEndpoint` after a validated key is persisted, carrying the path and whether the profile previously had no usable key; verify with tests for first key, key replacement, rejected key, and user-id-only update.
- [ ] 2.2 Emit `tacticus_integration_configured` with the import path from `ImportV1ProfileEndpoint` when the import supplies a first usable key, and verify a test asserts the path differs from the direct path.
- [ ] 2.3 Emit `v1_import_completed` from `ImportV1ProfileEndpoint` with per-part outcomes plus bucketed parsed/skipped goal volumes, excluding `V1ImportIssue` message text; verify with a mixed-outcome test and a test asserting no free-text or player/guild name reaches the recorded event.
- [ ] 2.4 Emit `player_sync_completed` from `PlayerSyncEndpoint` on both the success and failure paths, deriving `is_first_sync` from whether a snapshot already existed; verify with tests for first sync, later sync, unchanged-content sync, missing key, upstream rejection, and upstream error.
- [ ] 2.5 Emit `goals_created` once per successful request from `CreateGoalEndpoint` (single) and `CreateCombinedGoalsEndpoint` (combined, bucketed count); verify a combined request producing many goals reports exactly one event, and that a rejected request reports none.
- [ ] 2.5a Derive the shape properties — `entity_type`, the distinct `goal_types` and `farming_strategies` sets, `project_assignment`, and `created_status` — from the created goals rather than the request, so a default-project fallback and the active/paused outcome are reported as they actually resolved; verify with tests covering a single goal, a combined request of several kinds, and a request that falls back to the default project.
- [ ] 2.5b Derive the optional-feature properties — three-state `acquisition_sources_selected`, the distinct `acquisition_source_kinds`, `has_farming_location_override`, and `has_dependencies` — and verify with tests for an explicit source selection, an eligible goal left at the unrestricted default, a goal kind to which sources do not apply, an override present and absent, and a combined request with and without dependency chains.
- [ ] 2.5c Verify the distinct sets are order-independent with a test asserting two requests creating the same goal kinds in a different order report an identical set value.
- [ ] 2.6 Emit `project_activated` from `ActivateProjectEndpoint` only when the activated project was not already active; verify with tests for a genuine activation, a re-activation of the already-active project, and a failed activation.
- [ ] 2.7 Route every new emission through `ProductAnalyticsReporter.TryReport` with the event name, and verify a test asserts a throwing analytics implementation still leaves each triggering request with its normal successful response.

## 3. Test support

- [ ] 3.1 Extend `RecordingProductAnalytics` with the five new methods and their recorded properties, and verify the existing test suite compiles.
- [ ] 3.2 Add a `ProductAnalyticsTests` case asserting that no recorded event property, across every new event, contains an account id, display name, email, API key, Tacticus user id, guild name, project id, goal id, goal name, targeted unit id, or any goal target value — one test that fails if any future property leaks.
- [ ] 3.3 Add a test asserting every reported quantity is a bucket label from the fixed set rather than a raw number.

## 4. Gates

- [ ] 4.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` and verify it reports no changes.
- [ ] 4.2 Run `dotnet build TacticusPlanner.slnx -c Release --no-restore` and verify it succeeds with no new warnings.
- [ ] 4.3 Run `dotnet test TacticusPlanner.slnx -c Release --no-build` and verify all tests pass.
- [ ] 4.4 Confirm `artifacts/openapi/TacticusPlanner.Api.json` is unchanged by this change — no endpoint or contract was touched, so any diff here means something was added that should not have been.

## 5. Deferred / out-of-session

- [ ] 5.1 Verify against a live PostHog project that all five events arrive with the expected property names and bucket labels, using the full local stack via the workspace Aspire AppHost. Requires a real Tacticus API key and a real V1 profile, so it cannot be completed from the automated suite.
- [ ] 5.2 Coordinate merge order with the companion `tacticus-planner-apps` change `expand-analytics-event-taxonomy`: this half merges and deploys first. No contract is shared, so the apps half is not blocked on anything beyond the events existing in the destination.
