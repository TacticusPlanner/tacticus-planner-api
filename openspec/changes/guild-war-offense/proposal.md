## Why

V1's Guild War Offense page (`/plan/waroffense2`) lets a player mark units as deployed during a war so they can see which offense teams are still usable. V2 has the teams and the roster, but no Guild War game data and nowhere to keep a war. The Tacticus API still returns no Guild War data, so the war stays manual, but V2 can do more than V1: log each attack with its zone, slot, lineup and result, cap the lineup at the rarity of the slot being attacked, and keep every war as history. The plan is the "Guild War Offense V2 plan" doc (rows 1 to 6 and 24 Agreed; 15, 16 and 25 Later).

Companion change: `guild-war-offense` in `tacticus-planner-apps`. This API half applies first.

## What Changes

- New served catalog dataset `guild-war`, built from the datamine `globalconfig/guildWar.json`: one record per live season (A to D) with its zones and their layout, buffs and scores, the four zone tiers and their five slot rarity caps, the six battlefield levels (which tier each zone is at that level, and the per-attempt debuff), the zone buffs and their values, the map pool, the lineup scoring table and the per-rarity power caps.
- New profile-owned wars and attacks:
  - `GET /api/v1/guild-wars/current`, `PUT /api/v1/guild-wars/current` (season and battlefield level; creates the war when there is none), `POST /api/v1/guild-wars` (starts a new war, closing and keeping the current one), `GET /api/v1/guild-wars` (past wars, newest first, paged).
  - `POST /api/v1/guild-wars/current/attacks`, `PUT` and `DELETE /api/v1/guild-wars/{warId}/attacks/{attackId}` (delete is Undo).
- An attack keeps the zone and slot, the rarity cap the server derives from them, the player's lineup with each unit's progression at that time, an optional result (won or lost, with units lost), and optional enemy details (enemy lineup with rarity and rank, which enemy units were killed or left damaged, active zone buffs, map).
- A war's tokens used and deployed units are derived from its attacks. A unit can be in one attack per war, and a war holds at most the season's `maxAttempts` attacks.
- Wars and attacks are private to the account: deleted with the profile and carried by the staging data transfer. No pooling across players (plan row 25 is Later) and no V1 import.

## Capabilities

### New Capabilities

- `guild-war-dataset`: the served `guild-war` catalog dataset, its shape and its derivation from the datamine.
- `guild-war-offense`: wars and attacks, their endpoints, rules, history and profile lifecycle.

### Modified Capabilities

None.

## Impact

- `TacticusPlanner.GameCatalog`: `globalconfig/guildWar.json` joins the datamine allowlist; new `GuildWar` models, denormalizer and validation; `guild-war` joins `GameCatalogDatasets.Served`. The manifest snapshot gains one dataset and the derived `schemaHash` moves.
- `TacticusPlanner.Domain`: new `GuildWars` domain (war, attack, lineup and enemy records, limits).
- `TacticusPlanner.Persistence`: new `guild_wars` and `guild_war_attacks` tables, query filters and migration `AddGuildWars`.
- `TacticusPlanner.Api`: new `Features/GuildWars` and the `guild-war` catalog endpoint; `Features/StagingTransfer` carries wars and attacks.
- `artifacts/openapi`: the new endpoints and the dataset envelope.
