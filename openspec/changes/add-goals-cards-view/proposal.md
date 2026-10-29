## Why

The Goals page is getting a Cards view again (feedback `PLAN-026`). It becomes the default, with the table still available through a Table/Cards switch. Players want the choice to follow them across devices, so the client needs to store the preference on the account, not in one browser. User settings are already the account-wide preference store, so the preference goes there.

## What Changes

- User settings gain a `goalsView` value that is exactly `Cards` or `Table`, a stable id with no display text. A profile that has never chosen one reads as `Cards`, and so does any stored value saved before this field existed.
- A user-settings update requires `goalsView` together with `dailyEnergy`, `xpBookRarity` and `revision`, and persists it under the existing revision check. A missing, empty or unsupported value is rejected as a validation error and changes nothing. **BREAKING** for any client that sends the old request shape. The only consumer is the companion apps change, and the two merge back to back.
- The OpenAPI artifact gains `goalsView` on the user-settings request and response.

Companion change: `tacticus-planner-apps` `add-goals-cards-view` (the Cards/Table switch, the card layout, and the client that reads and writes `goalsView`). This API half applies first.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `user-settings`: adds the `goalsView` preference (read default `Cards`, required on update, validated against `Cards`/`Table`).

## Impact

- `src/TacticusPlanner.Domain/UserSettings/UserSettings.cs`: `UserSettingsData` gains `GoalsView` with default `Cards` and a supported-values set.
- `src/TacticusPlanner.Api/Features/UserSettings/`: a `GoalsView` enum, `UserSettingsResponse` (with read normalization), `UpdateUserSettingsRequest` (required), and the PUT handler.
- `src/TacticusPlanner.Persistence/Migrations/`: a generated migration that only updates the model snapshot for the new owned-JSON property. No DDL and no data change.
- `artifacts/openapi/TacticusPlanner.Api.json`: regenerated.
- Tests: `UserSettingsEndpointTests`, plus a Postgres test that reads legacy settings JSON without the field.
- No infra or configuration change.
