# quest-events-dataset Specification

## Purpose

Serves the battle data of every Quest Event (one record per featured hero, 17 battles each) and the shared 17-crate point track, so clients can show, score and plan quest battles from catalog data.

## Requirements

### Requirement: The catalog serves a `quest-events` dataset with a fixed shape

The game catalog SHALL serve a dataset keyed `quest-events` as a plain array at `GET /api/v1/game-catalog/quest-events`, without authentication, in the shared dataset envelope and with a manifest hash entry. It SHALL be built from the pinned datamine copy `gameconfig/liveEvents/linearHeroEvent.json` (one Quest Event per featured hero: `unitId`, `allowedFactions`, the `enemyFaction` of `trackingAdditionalData`, and per battle `BoardId`, `SpawnPointsSet`, `lightningVictory`, `Objectives`, the `loot.chanceOf` drop `upgradeId%num/denom`, and `enemies` as `npcId:progressionIndex`). It is not served directly. A battle without a readable drop or enemy SHALL fail startup naming `quest-events` and the quest. Each record SHALL have exactly these fields. The server sends ids and numbers only; the client resolves names, portraits, board images and objective wording.

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
    enemies: { id: string, ladderIndex: int, count: int }[],
    objectives: { type: string, target: string | null, score: int }[],
    loot: { upgradeId: string, chanceNumerator: int, chanceDenominator: int }
  }[]                                 // ordered by number
}
```

Records SHALL be ordered by `featuredCharacterId` (ordinal). `tier` SHALL be the rarity at the raw tier `index` (0 is Common, 5 is Mythic).

#### Scenario: Cyrus battle 1

- **WHEN** a client fetches `quest-events`
- **THEN** the `astarCyrus` record has `allowedFactionIds` `["AdeptusAstartes", "Ultramarines", "SpaceWolves", "BlackTemplars"]`, `enemyFactionIds` `["Necrons"]`, and a first battle `{ number: 1, tier: "Common", tierPosition: 1, deploySlots: 3, boardId: "C3_70", spawnPointsSet: 6, lightningVictoryTurns: 3, enemies: [{ id: "necroNpc2FlayedOne", ladderIndex: 1, count: 3 }, { id: "necroNpc1Warrior", ladderIndex: 1, count: 4 }], objectives: [{ type: "NoDamageTakenByAnyHero", target: null, score: 100 }, { type: "NotDamageType", target: "Power", score: 100 }], loot: { upgradeId: "upgDmgC001", chanceNumerator: 3, chanceDenominator: 4 } }`

#### Scenario: Cyrus battle 17

- **WHEN** the same record's battle 17 is read
- **THEN** it has `tier` `Mythic`, `tierPosition` 1, `deploySlots` 5, `boardId` `LE3_24`, `lightningVictoryTurns` 4 and `loot` `{ upgradeId: "upgHpM001", chanceNumerator: 1, chanceDenominator: 6 }`

#### Scenario: Coverage

- **WHEN** the dataset is served from datamine `v1.43.157.2989`
- **THEN** it has records for `astarCyrus`, `astarEradicator`, `darkaSternguard`, `thousSekhetar` and `votanChampion` only, each with 17 battles; the three V1 quests are unchanged

### Requirement: Deploy slots follow the tier position rule

`deploySlots` SHALL be derived from `tier` and `tierPosition` by this fixed rule, not read from the raw file:

| Tier | Positions 1, 2, 3, 4, 5 |
|---|---|
| Common | 3, 4, 5 |
| Uncommon | 3, 4, 5 |
| Rare | 3, 3, 3, 4, 5 |
| Epic | 3, 4, 5 |
| Legendary | 5, 5 |
| Mythic | 5 |

`spawnPointsSet` SHALL still be served unchanged; it is not used to derive slots.

Assumptions:

- The rule is the same for every quest (confirmed by Severyn). Legendary position 1 is 5, as Nubari's in-game screen shows, not the 4 first given.
- The featured hero takes one of the slots.

#### Scenario: Rare tier

- **WHEN** Cyrus's battles 7 to 11 are read
- **THEN** their `tierPosition` values are 1 to 5 and their `deploySlots` are 3, 3, 3, 4, 5

### Requirement: Enemies are grouped and indexed like lre-battles

A battle's `enemies` SHALL be built from the raw `enemies` list by merging entries with the same `name` and `progressionIndex` into one `{ id, ladderIndex, count }`, in order of first appearance. `ladderIndex` SHALL be the 1-based stat ladder index, as defined for `lre-battles`. A raw entry with a null `progressionIndex` SHALL be served with `ladderIndex` 1, as V1 read it. Board pickups (`powupHealing`, `powupDamage`, `powupArmor`) SHALL stay in `enemies`; the client tells them apart by the NPC's served classification.

#### Scenario: Grouping keeps order

- **GIVEN** a raw list of 3 `necroNpc2FlayedOne` at 1, then 4 `necroNpc1Warrior` at 1
- **THEN** the served list is FlayedOne ×3, then Warrior ×4

#### Scenario: Same NPC at two indexes

- **GIVEN** Cyrus battle 3's raw list, with `necroNpc2FlayedOne` at 3 and `necroNpc1Warrior` at 2
- **THEN** each is one entry, and `necroBossPlasmancerLHE` at 3 is a separate entry with count 1

#### Scenario: Power-up without an index

- **GIVEN** Nubari battle 9's raw `{ name: "powupArmor", progressionIndex: null }`
- **THEN** it is served as `{ id: "powupArmor", ladderIndex: 1, count: 1 }`

### Requirement: Objectives are served as typed records

Each raw objective SHALL be served as `{ type, target, score }` from `ObjectiveType`, `ObjectiveTarget` (null when absent) and `Score`, in raw order. `type` SHALL be one of `NoDamageTakenByAnyHero`, `NotTrait`, `NotFaction`, `NotDamageType`, `DamageType`, `HasRangedAttack`, `MaxHits`, `MinHits`, `UseNoSummons` and `DoNoHealOrRepair`. `target` SHALL be a trait id for `NotTrait`, a faction id for `NotFaction`, a damage type for `NotDamageType` and `DamageType`, a whole number written as a decimal string for `MaxHits` and `MinHits` (the raw file stores a number), and null for the others.

#### Scenario: Hit cap

- **GIVEN** a raw objective `{ ObjectiveType: "MaxHits", ObjectiveTarget: 3, Score: 100 }` (Nubari battle 7)
- **THEN** it is served as `{ type: "MaxHits", target: "3", score: 100 }`

### Requirement: The catalog serves a shared `quest-crates` dataset

The catalog SHALL serve `quest-crates` at `GET /api/v1/game-catalog/quest-crates` as a plain array of exactly 17 records, built from the raw `quest-crates.json` (ported from the crate table on the wiki's Quest page, which reflects the August 2025 17-crate track) and shared by every quest:

```
{
  number: int,                         // 1..17
  points: int,                         // points needed to open the crate
  rewards: { type: string, min: int, max: int, rarity: string | null, maxRarity: string | null }[]
}
```

Records SHALL be ordered by `number`, with `points` strictly ascending, and crate 17 SHALL need 8,800. `type` SHALL be one of `Coins`, `RaidTicket`, `XpBook`, `AbilityBadge` and `AscensionOrb`. `min` and `max` are the amount range (equal when the amount is fixed, `1 <= min <= max`). `rarity` SHALL be set for `XpBook`, `AbilityBadge` and `AscensionOrb` and null otherwise; `maxRarity` SHALL be set only when the reward is a random badge across a rarity range (for example Common to Uncommon), and SHALL NOT be below `rarity`. Badges and orbs are for the featured hero's alliance, which the client resolves. No reward carries a display name or icon.

#### Scenario: Last crate

- **WHEN** a client fetches `quest-crates`
- **THEN** it has 17 records and record 17 has `points` 8800
- **AND** record 17 includes `{ type: "Coins", min: 8000, max: 9000, rarity: null, maxRarity: null }` and `{ type: "AbilityBadge", min: 2, max: 2, rarity: "Common", maxRarity: "Uncommon" }`

### Requirement: The raw quest data is validated at load

The loader SHALL fail startup, naming the dataset and the quest, when:

- `quest-events` or `quest-crates` is missing or empty;
- `featuredCharacterId` is not a catalog character, or appears twice;
- an allowed or enemy faction is not a catalog faction id;
- a quest does not have exactly 17 battles numbered 1 to 17, split 3, 3, 5, 3, 2, 1 across tier indexes 0 to 5 in order;
- an enemy id is not in `npcs`, or an `upgradeId` is not in `upgrades`;
- an objective `type` is not listed, a required `target` is missing, or a `MaxHits`/`MinHits` target is not a whole number of at least 1;
- a loot chance is not `0 < numerator ≤ denominator`;
- a quest's maximum score, 17 × (300 medals + 100 Lightning Victory) plus the sum of its objective scores, is not equal to the `points` of the last `quest-crates` record;
- `quest-crates` is not 17 records with ascending `points`, or a reward has an unknown `type`, an invalid amount range or an invalid rarity range.

It SHALL NOT check `ladderIndex` against the NPC's ladder, as for `lre-battles`.

#### Scenario: Shipped data loads

- **GIVEN** the shipped raw files
- **WHEN** the catalog loads
- **THEN** loading succeeds, and every quest's maximum score is 8,800

#### Scenario: Wrong tier split

- **GIVEN** a quest whose Rare tier has 4 battles
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `quest-events` and that quest

#### Scenario: Score total drifts

- **GIVEN** a quest whose objectives sum to 1,900 instead of 2,000
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming the quest and its total 8,700
