## Context

See proposal.md - Why. `GameCatalogDenormalizer.BuildRewardLocations` (in
`Denormalization/GameCatalogDenormalizer.cs`) walks every campaign battle's
`Guaranteed`/`Potential` rewards and indexes them by reward id into
`RewardLocation` records, explicitly skipping `rewardId == "gold"`:

```csharp
void Add(string rewardId, RewardLocation location)
{
    if (string.IsNullOrWhiteSpace(rewardId) || string.Equals(rewardId, "gold", ...))
    {
        return;
    }
    ...
}
```

`ResolveLocations` then turns each `(rewardId, battleId)` group into a
`GameCatalogFarmLocation`. Gold never enters this pipeline, so no farm
location can report what a battle pays out. `UpgradeDenormalizer` and
`CharacterDenormalizer` both call `BuildRewardLocations`/`ResolveLocations`
via the shared `GameCatalogDenormalizer` partial, so a fix here covers both
upgrade-material and character-shard farm locations without touching either
denormalizer file directly.

## Goals / Non-Goals

**Goals:**
- Every served `GameCatalogFarmLocation` carries the expected gold payout of
  its battle, computed once, consistently, regardless of which resource(s)
  that battle also drops.

**Non-Goals:**
- Changing how gold itself is farmed, spent, or displayed anywhere else in
  the product — this is purely a new field riding along on the existing
  farm-location projection.
- The Extremis-visibility half of the companion recommendation-quality fix.
  That consumes data (`campaign-events-progress`) already served today; it
  needs no catalog or endpoint change and is entirely in the companion
  `tacticus-planner-apps` change.

## Decisions

**Index gold per battle, separately from the existing per-reward index.**
`BuildRewardLocations`'s `Add()` keys locations by reward id because a
resource can have many farm locations; gold isn't a farmable resource, it's
an attribute of the battle itself. Rather than forcing gold through the same
reward-id index (which would require inventing a synthetic reward id and
then filtering it back out downstream), build a second, small
`Dictionary<string battleId, decimal? expectedGold>` from each battle's
`Rewards.Guaranteed` entries whose id is `gold`, alongside the existing
`rewardLocations` dictionary, both inside `BuildRewardLocations`. `ResolveLocations`
already receives one lookup dictionary (`dropChanceById`); it gains this
second one and stamps `expectedGold` onto every `GameCatalogFarmLocation` it
returns by looking up `location.BattleId` — no change to the grouping/
consolidation logic that produces effective rates.

**`expectedGold = (min + max) / 2`, matching V1's semantics, not the raw range.**
V1's `CampaignsService.selectBestLocations` sorts by a single `expectedGold`
number; a range doesn't sort. Only `Guaranteed` gold rewards contribute —
`Potential` gold entries do not appear in the raw campaign-battle datasets
(spot-checked across the affected files), so there is currently no
probabilistic-gold case to fold in. Null when a battle has no guaranteed
gold entry at all, distinguishing "no gold reward" from a real `0`.

**One value per battle, not per farm location.** Two different materials
dropped by the same battle (e.g. a guaranteed drop plus a separate
probabilistic drop consolidated into two `GameCatalogFarmLocation`s for two
different resources) both cite the same `expectedGold`, since it describes
what raiding that battle pays regardless of which material a given location
record is about. This matches the spec's added scenario and keeps the
computation independent of the existing per-resource consolidation.

## Risks / Trade-offs

[Served dataset schema changes] → additive field only, no removed/renamed
field; existing consumers ignore unknown fields by construction of the
manifest/content-hash scheme, and the companion apps change updates the
zod payload schema in the same paired change so validation doesn't reject
the new field.

[Manifest content hash changes for every dataset containing farm locations]
→ expected and desired: the hash changing is exactly what causes clients to
refetch the corrected data; call this out in tasks.md so the snapshot test
update isn't mistaken for a regression.

## Migration Plan

No EF Core migration — this is a denormalization-time projection change
over existing embedded catalog data, not a change to stored player data.
Ships as a normal deploy; the next catalog manifest fetch picks up the new
field and updated content hash.
