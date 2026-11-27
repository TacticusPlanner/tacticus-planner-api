## Why

V1's Survival page (`/plan/survival`) is one of the most used V1 features V2 still lacks (3,034 views from 1,118 users in the 30 days to Nov 17). V1 reads six hand-extracted JSON files and keeps the team in V1 user data. V2 needs the same game data served from the pinned datamine, Survival dates in the event calendar, and a way to tie a team to one Survival event. The plan is the "Survival V2 rebuild plan" doc (13 options Agreed; suggested teams and rewards progress inputs Later).

Companion change: `add-survival-events` in `tacticus-planner-apps`. This API half applies first.

## What Changes

- New served catalog dataset `survival-events`, one record per datamine seasonal event with `includesSurvival`: identity and calendar dates, featured hero, currency, team rules (allowed factions, character and MoW caps, stamina, stat boost, Last Stand, global summon, persistent rarity-ups, featured hero stats), waves (grouped enemies, clone waves, rarity-up and power-ups on clear, score), scoring (medals by wave, points per kill, clone and skipped-turn score, rarity milestones), the points each source pays, milestones, progress chests, missions and offers.
- The datamine pin adds `liveEvents/seasonalEvent.json`, `shop/offers.json` and `shop/realMoneyProducts.json`.
- Event calendar: a `survival` definition (type `Survival`, no recurrence, required parameter `survivalEventId`) and occurrences for May, September and December 2026 from the Tacticus wiki. Crescendo A and B stay undated.
- Teams: a nullable `survivalEventKey` on create, update and read. When set, the team must be Horde only and follow the event's rules; broken rules come back as the new codes `survivalModeRequired`, `unknownSurvivalEvent`, `survivalFeaturedCharacter` and `survivalTooManyCharacters` (plus the existing `mowsNotAllowed` and `factionNotAllowed`).
- V1 import: V1 `survivalTeams` become Horde teams tied to their event, named `Survival · December 2026`.

## Capabilities

### New Capabilities

- `survival-events-dataset`: the served `survival-events` catalog dataset, its shape and its derivation from the datamine and the calendar.

### Modified Capabilities

- `personal-teams`: the `survivalEventKey` field and the Survival rules.
- `v1-team-import`: V1 `survivalTeams` are imported.

## Impact

- `TacticusPlanner.GameCatalog`: three datamine files join the allowlist; new `SurvivalEvents` models, denormalizer and validation; `survival-events` joins `GameCatalogDatasets.Served`; the event shop's ladder and mission readers take the dataset they report errors for. The manifest gains one dataset, `event-definitions` gains `survival`, and the derived `schemaHash` moves.
- `TacticusPlanner.Domain`: `Team.SurvivalEventKey`, `TeamSurvivalFacts` and five rule codes.
- `TacticusPlanner.Persistence`: `teams.survival_event_key` and migration `AddTeamSurvivalEventKey`.
- `TacticusPlanner.Api`: the `survival-events` catalog endpoint, the team contracts, the staging transfer and the V1 team import.
- `artifacts/openapi`: the new endpoint, the team field and the rule codes.
