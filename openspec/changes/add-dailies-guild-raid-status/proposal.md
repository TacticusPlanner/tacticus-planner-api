## Why

The Dailies Guild Raids page cannot show the guild's live raid position because V2 exposes guild membership but no normalized current-raid contract. A small status endpoint lets the app present the time-sensitive boss context without importing V1 analytics or exposing raw hit history.

## What Changes

- Add an authenticated current Guild Raid status endpoint for the caller's registered guild.
- Fetch and persist normalized upstream raid source facts per guild and season, then project active-season or no-active-season responses; represent refresh failures through retained stale data or mapped HTTP errors.
- Index persisted hits for a server-side current-user query that filters by guild season and the caller's existing hashed Tacticus user identity without loading other members' hits.
- Derive the current boss, tier/set progress, HP, prime modifiers and remaining thresholds from the persisted season data and raid-boss catalog.
- Return observation/freshness metadata and nullable season timing; the read endpoint always serves the latest persisted observation without calling upstream, a separate forced-refresh endpoint performs the upstream sync under a short per-guild cooldown, and collapses concurrent refreshes.
- Keep raw hit history, member performance, rankings, and team recommendations outside this contract.
- Coordinate the matching `add-dailies-guild-raid-status` change in `tacticus-planner-apps`; the API side applies first.

## Capabilities

### New Capabilities

- `guild-raid-current-status`: Defines the persisted Guild Raid observation model plus the normalized current-raid endpoint and its access, state, derivation, freshness, and failure behavior.

### Modified Capabilities

None.

## Impact

- Affects the Guild API feature area, persistence model and migration, the upstream Tacticus API client, raid-boss catalog lookups, dependency injection, endpoint tests, and generated OpenAPI.
- Uses the existing encrypted guild API token, persisted guild membership, and keyed Tacticus user-id hash; normalized raid source facts are stored by guild and season while derived status remains a read projection.
- Adds a frontend-consumed API contract paired with `tacticus-planner-apps`.
