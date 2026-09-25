## Why

The paired apps change `surface-goal-farming-guidance` adds an XP-book rarity choice to the Planning settings dialog so Level-goal guidance can express remaining XP as a book equivalent in the rarity the user actually farms. Planning settings are persisted server-side through `/api/v1/me/user-settings`, which today carries only `dailyEnergy`, so the field needs an API contract before the client can save it.

## What Changes

- Add `xpBookRarity` to the user-settings contract: one of `Common`, `Uncommon`, `Rare`, `Epic`, `Legendary`, `Mythic`, defaulting to `Legendary`.
- `GET /api/v1/me/user-settings` returns `xpBookRarity`; a profile with no stored value (new profile, or a row saved before this change) reads as `Legendary`.
- `PUT /api/v1/me/user-settings` requires `xpBookRarity` alongside `dailyEnergy` and `revision`, rejects any other value with a validation error, and persists it under the existing revision check.
- **BREAKING:** the PUT request gains a required field. The pre-production V2 policy permits this; the apps change is updated in the same pair and applies after this one.
- No new endpoint and no new table or column (settings are a nested JSON value).

## Capabilities

### New Capabilities

- `user-settings`: the account-wide planning settings contract, starting with the XP-book rarity preference and its default and validation rules.

### Modified Capabilities

None.

## Impact

`UserSettingsData`, `GetUserSettingsEndpoint`/`UpdateUserSettingsEndpoint` request/response records and validator, user-settings endpoint tests, the model snapshot (and an empty-DDL migration if EF requires one), and the regenerated `artifacts/openapi` artifact. Companion: `tacticus-planner-apps` change `surface-goal-farming-guidance` (dialog control, types, and consumption by Level guidance); this API half applies first.
