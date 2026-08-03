## Why

V2 can report a successful Tacticus player-data sync while retaining stale characters, inventory, and shard counts. The sync endpoint incorrectly treats the upstream `configHash`—the player's game-configuration version—as proof that player content is unchanged, so ordinary progression updates are skipped until the game configuration changes.

## What Changes

- Evaluate every successfully fetched player response for player-content changes even when its game `configHash` is unchanged.
- Recompute canonical per-chunk hashes and persist only chunks whose transformed content changed.
- Keep game-configuration metadata and upstream player freshness metadata distinct from player-content identity.
- Ensure a corrected sync automatically repairs previously stale server snapshots and advertises changed hashes to manifest-driven clients.
- Add regression coverage for shard, roster, and other player-data changes under an unchanged game configuration, as well as genuinely unchanged responses.

## Capabilities

### New Capabilities

- `player-data-sync`: Correctness requirements for detecting, persisting, and advertising upstream player-data changes independently of game-configuration changes.

### Modified Capabilities

(none)

## Impact

- Affects the authenticated player-sync endpoint, player-data transformation/hash comparison, and API endpoint tests.
- Retains the existing player-data manifest and chunk endpoint contracts; no frontend protocol change or database migration is expected.
- Existing web clients will receive changed chunk hashes through the current manifest-driven delta-sync path and refresh their IndexedDB records.
