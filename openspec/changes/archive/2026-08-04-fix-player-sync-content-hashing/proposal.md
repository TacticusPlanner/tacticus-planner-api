## Why

V2 can report a successful Tacticus player-data sync while retaining stale characters, inventory, and shard counts. The sync endpoint incorrectly treats the upstream `configHash`—the player's game-configuration version—as proof that player content is unchanged, so ordinary progression updates are skipped until the game configuration changes.

V2 also overestimates elite shard farming because the catalog emits a battle's guaranteed shard and simultaneous probabilistic bonus as separate farm locations. Consumers then treat them as alternative raid choices instead of one combined expected yield.

## What Changes

- Evaluate every successfully fetched player response for player-content changes even when its game `configHash` is unchanged.
- Recompute canonical per-chunk hashes and persist only chunks whose transformed content changed.
- Keep game-configuration metadata and upstream player freshness metadata distinct from player-content identity.
- Ensure a corrected sync automatically repairs previously stale server snapshots and advertises changed hashes to manifest-driven clients.
- Add regression coverage for shard, roster, and other player-data changes under an unchanged game configuration, as well as genuinely unchanged responses.
- Consolidate simultaneous rewards for the same resource and battle into one farm location whose expected rate includes both the guaranteed reward and probabilistic bonus.
- Add catalog regression coverage for elite shard nodes such as FoCE40 and SHME40, whose expected yield is `1 + 0.079 = 1.079` shards per raid.

## Capabilities

### New Capabilities

- `player-data-sync`: Correctness requirements for detecting, persisting, and advertising upstream player-data changes independently of game-configuration changes, plus correct catalog representation of the shard farming inputs used to evaluate synchronized player goals.

### Modified Capabilities

(none)

## Impact

- Affects the authenticated player-sync endpoint, player-data transformation/hash comparison, and API endpoint tests.
- Retains the existing player-data manifest and chunk endpoint contracts; no frontend protocol change or database migration is expected.
- Existing web clients will receive changed chunk hashes through the current manifest-driven delta-sync path and refresh their IndexedDB records.
- Affects game-catalog farm-location denormalization and catalog tests without changing the public farm-location schema.
- Catalog consumers receive one location per resource and battle, with `effectiveRate` carrying the combined expected yield when a guaranteed reward and bonus occur together.
