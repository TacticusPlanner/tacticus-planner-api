# Proposal

## Why

Game updates roll out to players over days: the 1.44.87.3088 datamine landed while most PC players were still on 1.43.157.2989. The API serves one catalog, so on the day it moves to 1.44 every player whose game has not updated yet gets plans for a game they are not playing. That 1.43 → 1.44 bump shows how much moves:

- 16 of 31 served datasets changed.
- The quest events rotated: Cyrus, Sternguard and Sekhetar out; Daras, Weirdboy and Parasite in.
- 11 NPCs changed stats, which feeds Legendary Event and raid estimates.
- Two units were added.

Serving the latest and the previous release side by side, and letting each player pick which one they plan with, removes that cliff.

Companion apps change: `catalog-version-window` in `tacticus-planner-apps` (per-version IndexedDB catalogs, the switcher in the account menu, the new-catalog banner, generated text from both releases). This API half applies first.

## What Changes

- **Two embedded datamine releases.** The catalog embeds two datamine releases, `latest` and `previous`, each with its own pin.
  - At startup both are loaded into immutable snapshots.
  - Curated data stays shared.
  - A datamine bump rolls the window: the old `latest` becomes `previous`, and the new release becomes `latest`.
- **New `GET /api/v1/game-catalog/versions` endpoint.** It lists the window as `{ gameVersion, version, role }`, with `role` being `Latest` or `Previous`.
- **Version-selectable manifest.** `GET /api/v1/game-catalog/manifest` takes an optional `gameVersion` query parameter.
  - Without it, the manifest is the latest one, exactly as today, so the deployed client is unaffected.
  - An unknown version is a 404.
- **Content-addressed dataset URLs for both releases.** `/api/v1/game-catalog/{dataset}/{envelopeHash}` resolves an envelope from either snapshot. Today only the current snapshot's hashes resolve.
- **Per-user catalog version setting.**
  - The stored value is `Latest` (the default) or an exact game version.
  - `GET /api/v1/settings` returns the stored value and the effective game version. A pin that has left the window is effectively `Latest`.
  - `PUT /api/v1/settings` accepts an optional `catalogVersion`. Omitting it keeps the stored value, so older clients are unaffected. A version outside the window is rejected.
- **Id validation accepts either release.** Goals, Legendary Event plans and run records, project event targets, and battle-progress overrides accept an id valid in either snapshot.
  - Rows that record a catalog version (Legendary Event plans and run records, roster snapshots) record the user's effective version.
  - Live-game paths stay on `latest`: Tacticus player-data sync and guild raid status.
- **`scripts/pull-datamine.cs` gains a roll mode.** The checks in `DatamineSourceTests` cover both pins.

No **BREAKING** changes: every existing request without the new parameters behaves as today.

## Capabilities

### New Capabilities

- `game-catalog-version-window`: which releases are served at once, how a client lists and selects them, how a version leaves the window, and which server paths use the selection versus always using `latest`.

### Modified Capabilities

- `game-catalog-datamine-source`: two pins (`latest`, `previous`) instead of one; the roll mode in `pull-datamine`; the game version is per release.
- `game-catalog-http-caching`: content-addressed envelope URLs and ETags resolve from either served release; the manifest takes `gameVersion`.
- `user-settings`: the `catalogVersion` setting, its validation and its effective value.

## Impact

- **`src/TacticusPlanner.GameCatalog`:**
  - `Data/datamine/{latest,previous}` with a pin each.
  - Embedded resource names gain a release folder in the path.
  - `GameCatalogLoader`, `GameCatalogRelease`, `DatamineFiles` and `IGameCatalogProvider` change.
- **`src/TacticusPlanner.Api/Features/GameCatalog`:** the new versions endpoint, the manifest parameter, and envelope lookup across snapshots.
- **Validators that read the catalog:** `GoalTargetValidationService`, `LegendaryEventCatalogValidator`, `ProjectTargets`, and the battle, campaign-event and incursion progress endpoints.
- **Catalog version stamps:** `LegendaryEventPlanWriter`, `LegendaryEventRunRecordRecorder` and `RosterSnapshotFactory` stamp the effective version.
- **Settings:** `Domain/UserSettings` gets a new JSON field; no EF Core migration is needed. `Features/UserSettings` changes.
- **Scripts and tests:** `scripts/pull-datamine.cs`, `DatamineSourceTests`, the manifest snapshot test, and the GameCatalog tests that load `previous`.
- **`artifacts/openapi`:** the versions endpoint, the manifest query parameter, and the settings fields.
- **Memory:** two snapshots are held instead of one. Served bodies are about 0.35 MB compressed per release.
- **Skill:** the workspace `tp-bump-datamine` skill moves from "replace the pin" to "roll the window". That's a separate workspace edit.
