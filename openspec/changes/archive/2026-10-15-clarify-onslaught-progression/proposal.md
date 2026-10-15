## Why

UserJot feedback "Clarify onslaught tier and sector progression" (Sep 17: "Do we need a Tier 4, when it's the same as Tier 3? … Expect tier 1 when increasing sectors, 4 when decreasing."; comment: "when you set an onslaught it doesn't seem to pull through when you're creating a goal for ascension. The tickbox tells you to go and edit onslaught settings."). The tier/sector UI fixes are client-only; this half fixes the inverse defect the investigation found. `GET /api/v1/me/player-data-overrides/onslaught-progress` answers Stone, Tier 1 for every alliance the player has never saved, which is indistinguishable from a real Stone 1 position. The client cannot infer "unset" on its own: `revision` is shared with campaign-event overrides on the same row. The client therefore treats every profile as having Onslaught progress: the goal create/edit prompt "Set your Onslaught progress to see its yield." never appears and new players silently get Stone 1 shard yields.

Companion apps change: `clarify-onslaught-progression` in `tacticus-planner-apps` (shows unsaved alliances as "not set", restores the goal prompt, and clarifies the sector/tier copy). This API half applies first.

## What Changes

- Add `isSet: boolean` to each alliance in the onslaught-progress GET and PUT response. It is `false` when no position has been saved for that alliance (Sector/Tier then carry the Stone 1 placeholder) and `true` once one has been saved through PUT or the V1 import.
- The PUT request accepts `null` for an alliance, meaning "not set": no override record is stored for it, so a player can save one alliance without inventing a position for the others.
- **BREAKING (pre-production)**: `OnslaughtAllianceProgressResponse` gains a required field and the request's alliance members become nullable.
- The regenerated OpenAPI artifact under `artifacts/openapi` gains the field.

## Capabilities

### New Capabilities

- `onslaught-progress-overrides`: the served shape of the per-alliance Onslaught progress override, including whether each alliance was ever saved.

### Modified Capabilities

None.

## Impact

- `src/TacticusPlanner.Api/Features/PlayerDataOverrides/OnslaughtProgressEndpoints.cs`.
- `artifacts/openapi/TacticusPlanner.Api.json`.
- Tests: `OnslaughtProgressEndpointTests`, `V1ImportEndpointTests`.
- No database, migration, configuration or infra change: "set" is derived from the presence of the alliance's existing JSON override record; a null alliance in PUT simply omits that record.
