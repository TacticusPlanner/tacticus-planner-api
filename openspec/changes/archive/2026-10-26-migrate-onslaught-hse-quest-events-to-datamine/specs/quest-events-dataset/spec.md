## MODIFIED Requirements

### Requirement: The catalog serves a `quest-events` dataset with a fixed shape

The game catalog SHALL serve a dataset keyed `quest-events` as a plain array at `GET /api/v1/game-catalog/quest-events`, without authentication, in the shared dataset envelope and with a manifest hash entry. It SHALL be built from the pinned datamine copy `gameconfig/liveEvents/linearHeroEvent.json` (one Quest Event per featured hero: `unitId`, `allowedFactions`, the `enemyFaction` of `trackingAdditionalData`, and per battle `BoardId`, `SpawnPointsSet`, `lightningVictory`, `Objectives`, the `loot.chanceOf` drop `upgradeId%num/denom`, and `enemies` as `npcId:progressionIndex`), plus the curated `quest-event-power.json` (unit id → battle number → enemy power). Neither is served directly. A battle without a readable drop or enemy, or a curated power for a quest or battle the game does not have, SHALL fail startup naming `quest-events` and the quest. Each record SHALL have exactly these fields. The server sends ids and numbers only; the client resolves names, portraits, board images and objective wording.

```
{
  featuredCharacterId: string,        // catalog character id (the game's unitId)
  allowedFactionIds: string[],        // raw order
  enemyFactionIds: string[],          // raw order
  battles: {
    number: int,                      // 1..17, from the game's battleNr
    tier: "Common" | "Uncommon" | "Rare" | "Epic" | "Legendary" | "Mythic",
    tierPosition: int,                // 1-based position inside its tier
    deploySlots: int,                 // units the player deploys, featured hero included
    boardId: string,                  // board image id, e.g. "C3_70"
    spawnPointsSet: int,              // the game's SpawnPointsSet, unchanged
    lightningVictoryTurns: int,       // the game's lightningVictory
    power: int | null,                // the game's enemy power for the battle, null when not recorded
    enemies: { id: string, ladderIndex: int, count: int }[],
    objectives: { type: string, target: string | null, score: int }[],
    loot: { upgradeId: string, chanceNumerator: int, chanceDenominator: int }
  }[]                                 // ordered by number
}
```

Records SHALL be ordered by `featuredCharacterId` (ordinal). `tier` SHALL be the rarity at the raw tier `index` (0 is Common, 5 is Mythic). `power` SHALL be the battle's curated power (taken from the in-game battle screen), and null when none is recorded.

#### Scenario: Cyrus battle 1

- **WHEN** a client fetches `quest-events`
- **THEN** the `astarCyrus` record has `allowedFactionIds` `["AdeptusAstartes", "Ultramarines", "SpaceWolves", "BlackTemplars"]`, `enemyFactionIds` `["Necrons"]`, and a first battle `{ number: 1, tier: "Common", tierPosition: 1, deploySlots: 3, boardId: "C3_70", spawnPointsSet: 6, lightningVictoryTurns: 3, enemies: [{ id: "necroNpc2FlayedOne", ladderIndex: 1, count: 3 }, { id: "necroNpc1Warrior", ladderIndex: 1, count: 4 }], objectives: [{ type: "NoDamageTakenByAnyHero", target: null, score: 100 }, { type: "NotDamageType", target: "Power", score: 100 }], loot: { upgradeId: "upgDmgC001", chanceNumerator: 3, chanceDenominator: 4 } }`

#### Scenario: Cyrus battle 17

- **WHEN** the same record's battle 17 is read
- **THEN** it has `tier` `Mythic`, `tierPosition` 1, `deploySlots` 5, `boardId` `LE3_24`, `lightningVictoryTurns` 4 and `loot` `{ upgradeId: "upgHpM001", chanceNumerator: 1, chanceDenominator: 6 }`

#### Scenario: Recorded battle power

- **GIVEN** the curated powers for Nubari's battles are 260, 447, 757, 1,183, 2,336, 3,540, 7,003, 8,379, 10,020, 15,822, 22,214, 33,847, 57,252, 98,715, 455,880, 1,459,769, 4,703,789
- **THEN** the `astarEradicator` battles serve those values as `power` in battle order, and Cyrus's and Kîmm's battles serve `power: null`

#### Scenario: Coverage

- **WHEN** the dataset is served from datamine `v1.43.157.2989`
- **THEN** it has records for `astarCyrus`, `astarEradicator`, `darkaSternguard`, `thousSekhetar` and `votanChampion` only, each with 17 battles; the three V1 quests are unchanged
