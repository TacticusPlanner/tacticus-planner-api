## Why

V2 keeps only the Guild Raid hits seen while someone refreshed the live season. Tacticus lists only the latest seasons, but a probe with a real guild key found it still serving every season since 70 (March 2025), with no promise to keep them. Guild raid performance needs that history, so capture ships first, ahead of any page or access rule that shows it.

## What Changes

- After a successful raid refresh, the API captures ended seasons, back to season 70, in the background: one request per season, resumable, never delaying the refresh.
- Past hits are stored from their own data. A hit that does not match today's season config no longer blocks its season, because the game edits the five season configs in place.
- A season becomes final only when fetched after a newer season started.
- Each stored hit also keeps the last four characters of the player's Tacticus id. The full id stays unstored.
- New `GET /api/v1/guild/raid/seasons` returns season numbers and capture status, with no hits or players.
- Captured hits are kept with no end date, until the guild's registration is deleted.

Not included: reading hits, member names or verification, access levels, importing V1 data, any UI, and a scheduler. Live refresh keeps its strict catalog check.

## Capabilities

### New Capabilities

- `guild-raid-season-capture`: which past seasons are captured, when they are final, what is stored, and the season status read.

### Modified Capabilities

- `guild-raid-current-status`: a successful refresh may start a background capture, which is separate from the shared refresh call and its response.
- `guild-authority`: `GET guild/raid/seasons` joins the reads authorized by the persisted guild link.

## Impact

- **API:** `Features/Guilds`, one new endpoint, regenerated OpenAPI artifact. Additive, so the deployed web client keeps working.
- **Database:** one migration: a capture table, two sync-state columns, a nullable id suffix on attacks, and `progression_index` made nullable.
- **Upstream:** one `/guild` request per capture run with work to do, plus one `/guildRaid/{season}` request per uncaptured season.
- **Companion:** `tacticus-planner-apps` change `guild-raid-capture` refreshes the committed API contract only. This change applies first.
