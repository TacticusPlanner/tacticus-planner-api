## Context

See `proposal.md` for the motivation and the specs for the behavior. Companion change: `guild-war-offense` in `tacticus-planner-apps`, sharing the `guild-war` dataset and the `/api/v1/guild-wars` endpoints. This half applies first.

Current state that shapes the approach:

- The game ships Guild War config in `globalconfig/guildWar.json`: `guildWarSeasonConfigs` keyed by season (`guildWarSeasonA` to `D`, plus `_staging`, `_fast` and `_ci2` test configs), `guildWarConfig` (eligibility, leagues and `maxHeroPowerCap`), `boardsToInclude` and `warZoneBuffs`.
- Seasons differ: Season D replaces `LandingPad1/2` and `AntiAirBattery` with `AntiAirBattery1/2` and `ComsStation`, and Season C has a different map pool. A record per season is therefore needed; one shared zone list would be wrong for D.
- Teams (`personal-teams`) already store `warOffense` lineups with a rarity cap. This change does not touch them: an attack copies the units it used.
- `LegendaryEventRunRecord` shows the pattern for a profile-owned record with nested jsonb, and `Team` the pattern for revisioned rows under the profile lock.

## Goals / Non-Goals

**Goals:**

- One served dataset with every Guild War fact Offense needs now and Counters, Defense and Zones need later.
- War state on the server, so a player can switch between phone and desktop mid-war (plan row 3).
- Every war and attack kept as the player's own history (plan row 24).

**Non-Goals:**

- Guild-wide views and sharing (row 15), Defense and Zones (row 16), pooling attack data across players (row 25), Counters.
- Any change to `teams` or another dataset.
- V1 import: V2 starts with no war.

## Decisions

### D1: One `guild-war` record per live season

The dataset serves the seasons whose key matches `guildWarSeason[A-Z]`, ordered by id; `_staging`, `_fast` and `_ci2` are test configs and are dropped. Each record repeats the shared `guildWarConfig.maxHeroPowerCap` as `powerCaps`, as `incursions` repeats its event-wide values, so a client reads one record. The game names no current season, so the war stores the season the player picked (D5).

Alternative considered: a single record with a shared zone list. Rejected because Season D's zones differ.

### D2: Served shape follows the game's ids, display stays client-side

Zones, tiers, buffs, abilities and boards are served by the game's ids (`Armoury`, `Elite`, `EnvArmourSupplies_Regional`, `EC1_09`). The client resolves names and icons. A zone also serves its `visualId`, because twin zones (`Bunker1`, `Bunker2`) share one visual and one name. Buff values are served as the game's variable arrays without interpretation: their index is not documented in the config, and the client shows the first value ("from +10% armour").

### D3: A buff's abilities are resolved by id or by rarity suffix

A buff names an `abilityId`. For most buffs the season's `abilities` has that key. For `EnvGarrison`, `EnvValkyrieStrike` and `EnvAngelsOfDeath` the game keys one ability per rarity (`EnvAngelsOfDeath_Epic`). The served buff lists every matching ability with its `rarity` (null for the unsuffixed one). A buff with no matching ability fails the load.

### D4: Wars and attacks are two tables

`guild_wars` holds one row per war; `guild_war_attacks` one row per attack with a cascading foreign key to its war. Attacks are written one at a time (log, edit, Undo), so a jsonb list on the war would make two devices conflict over unrelated attacks. The lineup and the enemy details are read and written whole with their attack and never queried inside, so they are jsonb columns of the attack row.

A partial unique index on `(profile_id)` where `ended_at IS NULL` keeps at most one current war per profile. Starting a war closes the current one and inserts the next under `LockCurrentProfileAsync`.

### D5: The war stores its season and battlefield level

`season_id` and `battlefield_level` are stored on the war and set by the player (PUT current, or when starting a war). The server checks the season is served and the level is one of the season's levels when they are written. A later catalog that drops the season does not touch stored wars.

### D6: The server derives the rarity cap

An attack sends `zoneId` and `slot` (0 to 4). The server reads the war's season and battlefield level, finds the zone's tier at that level, and stores `rarityCaps[slot]` on the attack. The stored cap survives a later change of battlefield level or catalog, and the client and server cannot disagree about it.

### D7: Tokens and deployed units are derived

A war serves `tokensUsed` (its attack count) and `deployedUnitIds` (every unit of every attack's lineup, characters then MoWs, in attack order). Nothing stores them, so an Undo can never leave a unit deployed. Creating or editing an attack is refused with `guildWarUnitDeployed` when one of its units is in another attack of the same war, and creating one is refused with `guildWarTokensSpent` when the war already holds the season's `maxAttempts` attacks (10 when the season is no longer served).

### D8: The lineup is what the client sends

The client has the roster with the player's overrides applied, so it sends each unit's `progression`, `rank`, `xpLevel` and ability levels. The server checks the ids are catalog characters and MoWs and the values are in range; it does not compare them with the synced roster.

### D9: Mutations return the whole war

Create, update and delete of an attack return the war as `GET` returns it, so a client replaces its cached war in one step. A war has at most 10 attacks, so the response stays small.

### D10: Past wars page by start time

`GET /api/v1/guild-wars` lists ended wars newest `startedAt` first, with the id as the tiebreak, in pages (`limit` 1 to 50, default 20, `after` an opaque cursor), each war with its attacks. Totals are the client's.

### D11: Staging transfer stays at format version 1

`StagingTransferBundle` gains `GuildWars`, each with its attacks, defaulting to empty. The importer deletes the target profile's wars and adds the bundle's with new ids.

### D12: Migration

Migration `AddGuildWars` creates both tables with cascading foreign keys (war to profile, attack to war and profile), which also covers account purge. There is no backfill. No existing table changes, so rollback is reverting the change.

## Risks / Trade-offs

- [The game does not say which season is live] → The player picks it once per war (new wars default to the previous war's season on the client). A wrong pick only changes the zone list and caps shown.
- [Buff value arrays are served uninterpreted] → The client shows the first value with "from"; Counters can interpret them later without a contract change.
- [The client sends unit progression, so it can be wrong] → It is the player's own record; nothing else reads it in this release.
- [A unit-deployed check across devices] → Writes run under the profile lock, so two devices cannot deploy one unit twice in a war.

## Migration Plan

Apply the migration on deploy, then ship the apps change. The apps change depends on these endpoints and the dataset, so its PR merges after this one.
