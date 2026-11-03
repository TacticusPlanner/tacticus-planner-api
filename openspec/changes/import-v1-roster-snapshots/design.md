## Context

V1 (`svehera/tacticusplanner`, `src/fsd/1-pages/input-roster-snapshots`) stores `rosterSnapshots: { base?: IRosterSnapshot, diffs: IRosterSnapshotDiff[] }`. `diffs[0]` is relative to `base`, and `diffs[i]` to `diffs[i-1]`. A diff carries only the fields that changed, per unit (`ISnapshotUnitDiff`: rank, rarity, stars, shards, mythicShards, xpLevel, active, passive, locked, equip0..2, equip0..2Level). `deletedDateMillisUtc` marks a snapshot V1 had moved to trash. V1 snapshot units: chars `{ id (snowprint), rank, rarity, stars, shards, mythicShards, activeAbilityLevel, passiveAbilityLevel, xpLevel, equip0..2, equip0..2Level }`; MoWs `{ id, rarity, stars, primaryAbilityLevel, secondaryAbilityLevel, shards, mythicShards, locked }`.

Companion: the apps half ships in `add-automatic-roster-snapshots` (import page checkbox and outcomes).

## Decisions

1. **Rebuild in memory, store full copies.** Port `resolveSnapshotDiff` and `fixSnapshot` to C#. Resolve every entry in order, trashed ones included, because later diffs build on them. Only the live ones are kept.
2. **Unit mapping.**
   - The V1 snowprint id becomes `UnitId`. An id unknown to the catalog drops that unit and is counted in the outcome's `droppedUnits`.
   - Rarity + stars become `progressionIndex` through the goal import's `ProgressionWire`. After `fixSnapshot`, a combination that still doesn't exist is clamped to the rarity's minimum stars.
   - The V1 `Rank` enum becomes `UnitRank` (`Stone1`=1 … `Adamantine3`). `Locked` (0) characters and `locked` MoWs are dropped, because V2 rosters hold owned units only.
   - Active/passive (character) and primary/secondary (MoW) levels become `abilities`, using the catalog unit's first active/first passive (or primary/secondary) ability id.
   - `equip0..2` and their levels become `equippedItems`, each placed in the catalog slot that matches the item's type. An item unknown to the catalog is dropped.
   - `xpLevel`, `shards` and `mythicShards` are copied. `xp = 0` and `appliedUpgradeSlots = []` mean unknown (see 4).
3. **Capacity.** `room = limit - count(Manual + V1Import)`. Live V1 snapshots are taken newest `dateMillisUtc` first. Duplicates are skipped before counting room. The rest beyond `room` get `skippedLimit`.
4. **Unknown fields.** The wire shape stays non-null. The rule "a `V1Import` snapshot's `xp` and `appliedUpgradeSlots` are unknown" lives in the spec, and the client renders them as "—". Alternative rejected: nullable fields on `RosterSnapshotResponse`. That would ripple through the LE run-record contract for one source's benefit.
5. **Idempotency key** is an immutable `importKey` column on the snapshot (nullable, set only for `V1Import`): `v1:<dateMillisUtc>:<original V1 name>`. Renaming changes `name` only, so a renamed import is still recognised as a duplicate. Deleting it does free the key, so a re-run imports it again. That's intended, since the user removed it.
6. **Catalog version** of an imported roster is `v1-import`. The `roster-snapshots` contract is modified to allow this exception (reconstructed values, no synced source), while keeping "full roster, never a diff".
8. **MoW XP level.** V1 MoW snapshots carry no `xpLevel`. Imported MoWs store `xpLevel = 0`, which means unknown, under the same convention as `xp`.
7. **Failure isolation.** A snapshot that throws during mapping gets `failed` and does not stop the others. An unreadable `rosterSnapshots` key fails the part with `invalid_roster_snapshots`. A missing or empty key skips it with `missing_roster_snapshots`.
