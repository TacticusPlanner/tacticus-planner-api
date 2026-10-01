## Context

`IsUnlockEligible(catalog, characterId)` is an extension on `GameCatalogSnapshot` used only by `GoalTargetValidationService`. The snapshot already exposes `ShopViews` (slots -> variants with `UnitId` and `Reward.Type`).

## Decisions

- Extend `IsUnlockEligible` in place (single caller) rather than add a new lookup: campaign nodes OR any shop variant with `UnitId == id` and `Reward.Type == "shards_" + id`.
- Match on `Reward.Type` (not just `UnitId`) so a mythic-only variant does not count; this mirrors the client's non-mythic offer filter.
- Ignore day, power-level and lock conditions: eligibility asks "can this ever be farmed", consistent with the client's permissive resolver. Per-offer validity is already checked by `AcquisitionSourceRules` (shop id must name a known shop).
- Keep the owned check and its message unchanged; reword only the eligibility message.

## Risks

- A shop variant that is permanently locked in-game would still enable Unlock. Acceptable; the goal is creatable and the estimate treats unselected sources as absent.
