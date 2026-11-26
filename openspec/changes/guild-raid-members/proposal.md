## Why

Guild raid performance needs two things V2 lacks. First, complete hits: V2 keeps only the hits seen while someone refreshed the live season, so every season loses the hits made after its last refresh. Second, trusted identities: a player's Tacticus user id is self-asserted, so anyone who knows the id of an unclaimed Leader gets that role in Planner at the next guild sync. Before Planner shows one player's raid data to another, it must hold the whole season and know who each member is.

Loading older seasons is left out on purpose. A probe found Tacticus still serving every season back to 70, and the raid meta changes often enough that old seasons are of little use. Leader-chosen loading of past seasons can follow once the API has a job library.

## What Changes

- The first refresh after a season ended reads that season once more, in the same request, so its last hits are kept. Nothing runs in the background and no older season is loaded.
- Hits of an ended season are stored from their own data, because the game edits the five season configs in place. Live refresh keeps its strict catalog check.
- Each stored hit also keeps the last four characters of the player's Tacticus id. The full id stays unstored.
- A claim on a guild member is verified or not. Officers and Members are verified automatically when their synced Tacticus name equals the name a leader assigned. Leaders, Co-Leaders and everyone else are confirmed by a verified leader.
- The first verified leader is whoever stored the guild's current key, if they are still Leader or Co-Leader. This also covers existing guilds.
- Verified leaders can assign names, confirm claims one by one or together, and reject a claim. Names belong to a player of the guild and outlast roster membership, so former members can be named too.
- **BREAKING (behavior):** deleting a guild registration, and replacing its key while it has a verified leader, require a verified Leader or Co-Leader. Response shapes stay compatible.
- A raid refresh also refreshes the roster when it is more than 24 hours old, so departures and demotions no longer wait for a manual sync.

Not included: loading older seasons, any background worker or scheduler, reading other players' hits, access levels, readiness opt-out, importing V1 data, and registration by Officers.

## Capabilities

### New Capabilities

- `guild-member-verification`: how a claim is verified, rejected or lost, who may manage claims and names, how guild players are identified, and the daily roster refresh.

### Modified Capabilities

- `guild-raid-current-status`: a refresh may read the season that just ended and refresh the roster; ended-season hits are stored from their own data; hits keep a partial player id.
- `guild-authority`: purge and key replacement need verification; the roster also refreshes during raid refreshes.
- `account-purge`: also removes verification and the assigned name.

## Impact

- **API:** `Features/Guilds`, `Features/TacticusIntegration`, `Features/PlayerData`, account purge; five new endpoints; additive fields on the guild response; regenerated OpenAPI artifact.
- **Database:** one migration: a `guild_players` table, four nullable columns on `guild_members`, three columns on `guild_raid_sync_states`, `finalized_at` on `guild_raid_seasons`, a nullable id suffix on attacks, and `progression_index` made nullable.
- **Upstream:** at most one extra `/guildRaid/{season}` request per guild and ended season, retried at most hourly, and one extra `/guild` request per guild and day. Both run inside a refresh that already calls Tacticus.
- **Companion:** `tacticus-planner-apps` change `guild-raid-members` adds the management screen and notices and refreshes the API contract. This change applies first.
