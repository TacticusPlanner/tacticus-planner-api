## Context

See proposal.md - Why. `PlayerDataTransformer.Progress.cs` builds three things from the same `response.Player.Progress.Campaigns` list (`PlayerDataTransformer.cs:47-54`):

- `campaignProgress` (standing campaigns) via `MapCampaign`, keyed by `(Id, Type)`.
- `campaignEventsProgress` (campaign events) via `MapCampaignEvent`, keyed by `(Id, Type)`.
- `BattleAttempts` via `campaigns.SelectMany(MapBattleAttempts)`, keyed only by `(TacticusCampaignId, BattleIndex)` — `MapBattleAttempts` never reads `campaign.Type`.

Tacticus reports a campaign event's Standard and Extremis tiers as two separate entries in `Campaigns`, sharing one `Id` but with different `Type` and independent `battles[]` (so `BattleIndex` starts over per tier). `MapCampaign`/`MapCampaignEvent` already handle this correctly because they process one `CampaignProgress` entry at a time and read its `Type`. `MapBattleAttempts` processes each entry the same way but drops `Type` when building `BattleAttemptRecord`, so once `SelectMany` flattens both tiers into one list, a Standard-tier and an Extremis-tier attempt at the same `BattleIndex` become indistinguishable.

`BattleAttempts` lives inside the `live_progress` jsonb column (`PlayerDataSnapshotConfiguration.cs:102-105`, `OwnsOne(...).ToJson("live_progress")` with `OwnsMany(live => live.BattleAttempts)` nested inside it) — there is no separate table or column per `BattleAttemptRecord` field.

Separately, `GameCatalogCampaignBattle.BattleIndex` (`Models/Campaigns.cs:41`) already exists and is already correct — `GameCatalogLoader.cs:263-268` assigns it per `{campaignGroupId, type}` track, in the group's stored battle order, via a running counter reset per `Type`. It's used today only to build `campaign-events-progress` server-side (`MapCampaignEvent`'s `completedIndices.Contains(battle.BattleIndex)` check). The served view type, `GameCatalogCampaignBattleView` (`Models/Campaigns.cs:95-114`), does not carry it — the client never receives a battle's `battleIndex` today.

## Goals / Non-Goals

**Goals:**

- Stop discarding tier information when building `BattleAttempts`, so a consumer can distinguish a campaign event's Standard and Extremis attempts.
- Expose the existing, already-correct `battleIndex` on served battles, so a consumer can resolve a tier-tagged battle-attempt record back to the exact battle — including an event-campaign challenge node, which `nodeNumber` alone cannot identify.

**Non-Goals:**

- Changing `campaign-progress` or `campaign-events-progress` — both already carry `Type` correctly; unaffected.
- Changing how `ActiveCampaignEventId` is derived — unaffected, unrelated field on the same chunk.
- Reworking the `live-progress` chunk's storage shape (jsonb-owned collection) — this is a field addition within the existing shape.
- Changing how `GameCatalogCampaignBattle.BattleIndex` is computed — it's already correct; only its absence from the served view changes.

## Decisions

**Add `Type` to `BattleAttemptRecord` rather than introducing a separate per-tier record type.** `BattleAttemptRecord` already mirrors `CampaignProgressRecord`'s `(TacticusCampaignId, Type)` identity pair for everything except this one field; adding `Type` keeps one record shape for every campaign kind (standing and event) instead of a parallel "event battle attempt" type, matching how `CampaignProgressRecord`/`CampaignEventProgressRecord` already differ only in their extra fields, not their identity fields.

**Populate `Type` for every campaign, not only event campaigns.** `MapBattleAttempts` takes one `CampaignProgress` entry (already scoped to a single `(Id, Type)`) and has `campaign.Type` in hand regardless of whether `IsEventCampaign` is true. Special-casing "only tag `Type` for event campaigns" would save nothing (the field would still need to exist on the record for every row) and would make the apps-side consumer branch on campaign kind instead of using one uniform `{campaignId, type, battleIndex}` key — see the companion apps change's design for why a uniform key is preferred there.

**No EF Core migration.** `BattleAttempts` is an EF Core JSON-owned collection (`ToJson`) nested inside the `live_progress` jsonb column, not its own table or column set. Adding a property to the CLR type changes what gets serialized into that existing jsonb value; it does not require a schema migration. Confirmed against `PlayerDataSnapshotConfiguration.cs`: none of the `OwnsOne`/`OwnsMany` chunks nested under a `ToJson()` root have their own migration history — only the top-level `player_data_snapshots` table and its non-JSON columns do.

**Companion apps change consumes `type` and `battleIndex` directly; no server-side pre-aggregation.** The alternative — having the API pre-split `BattleAttempts` into separate standing/event lists, or pre-resolve each event attempt's node number — was considered and rejected: the client already owns the battle catalog and already does its own `{campaignGroupId, nodeNumber}` → battle lookup for standing campaigns (`buildStandingBattleIndex`), so extending that same client-side pattern to `{campaignGroupId, type, battleIndex}` for every campaign keeps one mapping convention client-side instead of splitting it across two repos.

**Serve `battleIndex` on every battle, not only event-campaign battles.** Mirrors the `Type`-on-every-`BattleAttemptRecord` decision above for the same reason: a standing campaign's `battleIndex` already happens to equal `nodeNumber - 1`, so serving it there is a no-op for the client's existing standing-campaign logic, but special-casing "only serve it for event campaigns" would force the client to keep two different lookup strategies (one keyed by `nodeNumber - 1`, one keyed by a served `battleIndex`) instead of one uniform `{campaignGroupId, type, battleIndex}` lookup for every campaign kind.

## Risks / Trade-offs

[A client on an older cached `live-progress` chunk (synced before this change deploys) has `BattleAttemptRecord`s with no `type`] → Not a concern for this repo: `live-progress` is re-synced from Tacticus on every successful sync (see `player-data-sync`'s "Player content is evaluated on every successful upstream sync"), not incrementally patched, so the next sync after this change ships replaces the whole chunk with tier-tagged records. The companion apps change's schema handles a payload that predates this field only as a transitional deploy-ordering concern, per its own design.

[Standing campaigns tagging every `BattleAttemptRecord` with `Type` is a no-op cost for the vast majority of rows] → Accepted: the alternative (event-only tagging) was rejected above for the uniformity it would cost the apps side.

[Adding `battleIndex` to `GameCatalogCampaignBattleView` changes the campaign-battles dataset's serialized content, which shifts its manifest content hash] → Expected and benign, same as the prior paired change's `expectedGold` addition: `GameCatalogSnapshotTests`' `.verified.txt` baseline needs regenerating/promoting for the affected dataset hash (task 2.2 below), and every client re-downloads that dataset on next manifest check — standard behavior for any additive catalog field, not a special case.

## Migration Plan

No data migration — see "No EF Core migration" above. Ships as a normal backend deploy; per this repo's cross-repo convention, this API half applies first, and the companion `tacticus-planner-apps` change (same change name) applies after.
