## Why

The Dailies Guild Raids page cannot show the guild's live raid position because V2 exposes guild membership but no normalized current-raid contract. A small status endpoint lets the app present the time-sensitive boss context without importing V1 analytics or exposing raw hit history.

## What Changes

- Add an authenticated current Guild Raid status endpoint for the caller's registered guild.
- Fetch and normalize the upstream current-season response into active-season, no-active-season, and temporarily-unavailable states.
- Derive the current boss, tier/set progress, next boss, HP, prime modifiers and remaining thresholds from the raid-boss catalog.
- Return observation/freshness metadata and nullable season timing; keep a short-lived per-guild cache, support explicit refresh, and collapse concurrent refreshes.
- Keep raw hit history, member performance, rankings, and team recommendations outside this contract.
- Coordinate the matching `add-dailies-guild-raid-status` change in `tacticus-planner-apps`; the API side applies first.

## Capabilities

### New Capabilities

- `guild-raid-current-status`: Defines the normalized, cached current-raid endpoint and its access, state, derivation, freshness, and failure behavior.

### Modified Capabilities

None.

## Impact

- Affects the Guild API feature area, the upstream Tacticus API client, raid-boss catalog lookups, dependency injection, endpoint tests, and generated OpenAPI.
- Uses the existing encrypted guild API token and persisted guild membership; no new database table or migration is expected.
- Adds a frontend-consumed API contract paired with `tacticus-planner-apps`.
