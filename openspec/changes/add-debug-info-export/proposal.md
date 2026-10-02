## Why

Bug reports arrive as prose. Reproducing a planning, dailies or raids issue needs the reporter's actual roster, inventory, progress, goals and settings, and today the only way to get them is to ask the user for screenshots or walk them through the UI. A single authenticated endpoint that returns the profile's complete planner state as plain, readable JSON lets a user attach one file to a report and lets a maintainer trace the root cause from the data the user was actually looking at.

Companion apps change: `add-debug-info-export` in `tacticus-planner-apps` (account-card row, explanatory dialog, client context wrapper and file download). This API half applies first.

## What Changes

- Add `GET /api/v1/me/debug-info`: one call that composes, for the authenticated profile, the current player-data snapshot (sync metadata and all served chunks), the player-data overrides, every non-deleted goal including archived ones, every project, and the planning settings, reusing the projections the existing endpoints already serve.
- Strip personal and free-text content server-side: no account, profile, or Tacticus-integration data; the in-game player name is blanked; goal notes, project names and project notes are omitted. Projects keep id, membership and ordering so plan structure survives.
- No import counterpart, no encryption, no persistence or configuration. The endpoint is read-only and side-effect free.
- The regenerated OpenAPI artifact under `artifacts/openapi` gains the new endpoint and response contract.

## Capabilities

### New Capabilities

- `debug-info-export`: the composition, content and exclusions of the authenticated profile's debug-info export.

### Modified Capabilities

<!-- none: existing endpoints and their requirements are unchanged -->

## Impact

- `src/TacticusPlanner.Api/Features/DebugInfo` (new): endpoint and response composition over `PlayerDataSnapshots`, `PlayerDataOverrides`, goals, projects and user settings.
- Reuses chunk projections from `Features/PlayerData` and the goal, project, override and settings response shapes from their existing features; those features are not modified.
- `artifacts/openapi`: new operation and schemas.
- Tests: endpoint tests covering composition, PII and free-text exclusion, and the no-snapshot case.
- No database or migration changes. No new configuration keys. No infra changes.
