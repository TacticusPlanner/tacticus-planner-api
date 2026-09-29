## Context

`user_settings` stores a row per profile with a `Revision` concurrency token and a nested owned-JSON `Settings` value (`UserSettingsData`, currently only `DailyEnergy`, validated against a supported-tier set). GET lazily creates a defaults row; PUT replaces the whole `Settings` object under an exact-revision check. See proposal.md for motivation and the paired apps change `surface-goal-farming-guidance`, which shares the `/api/v1/me/user-settings` contract.

## Goals / Non-Goals

**Goals:** carry one validated `xpBookRarity` through GET/PUT with a Legendary default that also covers rows saved before the field existed.

**Non-Goals:** no XP-book values, conversion math, or display strings on the server (the client owns the rarity XP table and labels); no new endpoint, no change to `dailyEnergy` or revision semantics.

## Decisions

- **String property, not a CLR enum.** `UserSettingsData` gains `XpBookRarity` as a string defaulting to `"Legendary"`, validated against a supported-set constant, mirroring `DailyEnergy`/`SupportedDailyEnergy`. The wire values match the client's existing rarity ids (`Common`…`Mythic`). A CLR enum would materialize a missing JSON property as its zero member rather than Legendary, and would turn any future rarity change into a serialization concern; the string plus supported-set keeps the default explicit.
- **Read-side default instead of a backfill.** Existing rows have JSON without the property. The response mapping treats a missing/null/unsupported stored value as `Legendary`, so no data migration or backfill is needed and a bad stored value can never leak to the client. A test seeds legacy JSON to lock this in.
- **PUT requires the field.** PUT replaces `Settings` wholesale, so an optional field would silently reset a stored choice when an older client omitted it. Making it required (validator rejects null/empty/unsupported) keeps PUT a full-replacement contract; the apps change ships in the same pair and V2 permits the break.
- **Contract shape:** response `{ dailyEnergy: int, xpBookRarity: string, revision: long }`; request `{ dailyEnergy: int, xpBookRarity: string, revision: long }`. The OpenAPI artifact regenerates on build and is reviewed for the new property (typed as a string; the supported values are enforced by the validator, not the schema).

## Risks / Trade-offs

- [EF may materialize a missing JSON property differently than assumed, or throw] → verify with a test that writes legacy JSON directly and reads through the endpoint; fall back to nullable storage plus the read-side default if needed.
- [Model snapshot changes for the new owned property] → EF Core migrations apply automatically and the project requires the migration in the same change: generate it with `dotnet ef migrations add`, expect an empty Up/Down (JSON content only, no DDL), and inspect it to confirm no column or data change.
- [Older cached client PUTs without the field get a 400] → accepted; the pair applies API first and the apps change follows in the same release train, and the client already surfaces PUT errors in the dialog.

## Migration Plan

Deploy the API first; startup applies the (empty-DDL) migration. Existing rows keep working through the read-side default. Rollback is safe: the extra JSON property is ignored by older code.
