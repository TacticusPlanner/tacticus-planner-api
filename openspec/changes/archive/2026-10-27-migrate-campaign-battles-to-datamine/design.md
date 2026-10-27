## Context

The raw `GameCatalogCampaignGroup` record and everything built from it (battles, definitions, reward
locations, battle indices) stay. `CampaignDatamineReader.ReadDatamineCampaigns` builds it from the datamine plus
one curated file, keyed by the existing `CampaignBattleGroups` keys.

## Decisions

1. **Group metadata stays curated.** The game's `allowedFactions` and `requiredUnits` lists differ from the
   planner's allies and core characters in content or order for most groups, and the game has no field for the
   group's faction or release type. Moving them would change served values for no game reason.
2. **Battle ids keep V1's scheme.** Per type the curated prefix (`I`, `FoCME`, `AMS`) plus the game's
   `battleId`; an event challenge battle (`13B`) becomes `{prefix}C13B`. Ids and order equal V1 for all 1,316
   battles.
3. **Enemies** are the indexed units of the teams that are not `playerTeams` 1 (the player's allies, as in
   Death Guard's Black Legion helpers). Unindexed units are board power-ups. Counted per `npcId:index`, sorted
   by npc id, then index.
4. **Enemy stars and rank** follow V1's reading of the stat ladder: a boss index is 1-based, any other npc's is
   0-based and capped at the last row. This reproduces all but one of V1's 3,680 detailed enemies; the
   remaining one (I75's boss) is a game change.
5. **Drop chance ids** are derived as `{kind}_{difficulty}` from the reward and the campaign kind. The curated
   `drop-chances` dataset still supplies the rates.

## Risks

- The loader fails when the curated keys and `CampaignBattleGroups` disagree, so a new campaign needs both.
