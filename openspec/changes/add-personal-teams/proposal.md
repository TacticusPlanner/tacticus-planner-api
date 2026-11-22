## Why

V1 players keep reusable lineups for Guild War, Guild Raid, Tournament Arena, Horde, Campaign and Incursion, each with its own rules. V2 has no equivalent and V1 import leaves that data behind, so a player moving to V2 loses their teams (tracked in `TacticusPlanner/tacticus-planner-apps#26`, which also covers the later War Defense and War Offense tools). A personal team model is the foundation both of those tools build on.

Companion change: `add-personal-teams` in `tacticus-planner-apps`. This API half applies first.

## What Changes

- New profile-owned teams with a stable id, a name, an order, game modes, ordered characters and Machines of War each split into core and flex, a campaign, Incursion MoWs, notes and an optional rarity cap the team is prepared for.
- New endpoints: `GET` and `POST /api/v1/teams`, `PUT` and `DELETE /api/v1/teams/{teamId}`, and `PUT /api/v1/teams/order`.
- Per-mode rules are enforced on every create and update. War and Tournament Arena allow 5 core characters and 1 core MoW; flex units of either kind do not count. Campaign rules read `CampaignGroups`, and Incursion rules read the `allowedFactions` of the `incursions` dataset. **No catalog data changes.**
- A team that breaks a rule can exist only through import. Every read reports its broken rules as `issues`, and it must satisfy every rule the next time it is saved.
- New opt-in `teams` part on `POST /api/v1/account/v1-import`, default `false`. Imported teams keep their original V1 name as an immutable import key, so a re-run skips teams already imported, even renamed ones, skips V1 names taken by a team made in V2, and never overwrites a V2 team. The same key lets the later War Defense import find teams by their V1 name.
- Teams are deleted with the profile and carried by the staging data transfer.

## Capabilities

### New Capabilities

- `personal-teams`: the team record, its endpoints, order, limits, per-mode rules, reported issues and profile lifecycle.
- `v1-team-import`: the `teams` part of the V1 import. It covers reading `teams2`, resolving units, modes, campaigns and flex positions, re-runs, limits and per-team outcomes.

### Modified Capabilities

None. The existing V1 import parts and catalog datasets keep their behavior.

## Impact

- `TacticusPlanner.Domain`: new `Teams` domain and validation.
- `TacticusPlanner.Persistence`: new `teams` table, query filter and migration `AddTeams`, including the MoW flex index and the rarity cap. The migration is not yet in production, so it is amended rather than followed by a second one.
- `TacticusPlanner.Api`: new `Features/Teams`; `Features/V1Import` gains `V1TeamImportService`, the selection flag and `teamOutcomes`; `Features/StagingTransfer` carries teams.
- `artifacts/openapi`: the new endpoints, the selection flag and the outcome schema.
- Merges alongside `import-v1-roster-snapshots`, which edits the same V1 import request and response records.
- ADR 0008 is unchanged: Legendary Event teams stay owned by their plan.
