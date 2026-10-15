# mythic-quests-dataset Specification

## Purpose

Serves each character's mythic quests (tasks, targets, task parameters and structured rewards) from the game
catalog, so clients render quests from catalog data instead of shipping their own static copy.

## Requirements

### Requirement: The catalog serves a `mythic-quests` dataset

The game catalog SHALL serve a dataset keyed `mythic-quests` as a plain array at
`GET /api/v1/game-catalog/mythic-quests`, without requiring authentication, in the same dataset envelope as the
other served datasets. It SHALL be built from one authored raw source file, `mythic-quests.json`, whose content
is ported from V1's `mythic_quests/data/mythic-quests.json` `quests[]`; the raw file SHALL NOT be served
directly. V1's `terms[]` (task text templates) SHALL NOT be part of the catalog: task text is display text and
stays with the client.

#### Scenario: Dataset is served without authentication

- **WHEN** a client requests the `mythic-quests` endpoint
- **THEN** the array is returned without requiring authentication, wrapped in the same envelope as the other
  served datasets

#### Scenario: Dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `mythic-quests`, and the manifest snapshot test covers it

#### Scenario: The raw source has no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving the raw `mythic-quests.json` directly

### Requirement: Served mythic quest shape

Each served record SHALL carry exactly these fields:

- `unitId` (string): the character's catalog id.
- `quests` (array): the character's quests, ordered by ascending `number`. Each quest SHALL carry exactly:
  - `number` (integer, 1 to 5).
  - `tasks` (array, 1 to 4 entries, in source order). Each task SHALL carry exactly `locaKey` (string, the
    task-text template key, for example `SlayEnemiesWithHeroToTraitInOneBattle_description`), `target`
    (integer, at least 1) and `parameters` (object of string to string, copied unchanged from V1's
    `taskParameters`, for example `{ "trait": "Emplacement", "heroId": "blackAbaddon", "killAmount": "8" }`).
  - `rewards` (array, in source order) of reward records, defined in the next requirement.

Records SHALL be ordered by `unitId` (ordinal). V1's quest `name` (`mythic_blackAbaddon_01`), task `name` and
`characterName` SHALL NOT be served. The client derives the task text from `locaKey` and `parameters` using its
own templates; the server sends no display text.

#### Scenario: Abaddon's first quest

- **GIVEN** V1's Abaddon quest 1: task `SlayEnemiesWithHeroToTraitInOneBattle_description`, target 1, parameters
  `trait: Emplacement, heroId: blackAbaddon, killAmount: 8`
- **WHEN** the dataset is served
- **THEN** the `blackAbaddon` record's first quest has `number: 1` and one task with that `locaKey`,
  `target: 1` and those three parameters as strings

#### Scenario: Coverage

- **WHEN** the dataset is served
- **THEN** it has 117 records, each with exactly 5 quests numbered 1 to 5

### Requirement: Rewards are served as structured records

The server SHALL parse each V1 reward string (`<type>[:<amount>]`) into a record with `type` (string) and
`amount` (integer, at least 1; `1` when the source string has no `:<amount>`), plus exactly the id fields its
type needs:

| V1 reward string                       | `type`         | extra fields                              |
|----------------------------------------|----------------|-------------------------------------------|
| `mythicShards_<unitId>:<n>`            | `MythicShards` | `unitId`                                  |
| `heroAscensionOrbMythic_<alliance>:<n>`| `AscensionOrb` | `rarity: "Mythic"`, `alliance`            |
| `abilityTokenMythic_<alliance>:<n>`    | `AbilityBadge` | `rarity: "Mythic"`, `alliance`            |
| `xpMythic:<n>`                         | `XpBook`       | `rarity: "Mythic"`                        |
| `gold:<n>`                             | `Gold`         | none                                      |
| `<equipmentId>` (an `I_` or `R_` id)   | `Equipment`    | `equipmentId`                             |

`alliance` SHALL be one of `Imperial`, `Chaos`, `Xenos`. No reward record SHALL carry a display name or icon.

#### Scenario: Shards, orbs and XP

- **GIVEN** the rewards `mythicShards_blackAbaddon:4`, `heroAscensionOrbMythic_Chaos:2`, `xpMythic:1`
- **WHEN** the dataset is served
- **THEN** the rewards are `{type: MythicShards, amount: 4, unitId: blackAbaddon}`,
  `{type: AscensionOrb, amount: 2, rarity: Mythic, alliance: Chaos}` and
  `{type: XpBook, amount: 1, rarity: Mythic}`

#### Scenario: Equipment reward without an amount

- **GIVEN** Abaddon's quest 5 reward `I_Crit_M006`
- **WHEN** the dataset is served
- **THEN** that reward is `{type: Equipment, amount: 1, equipmentId: I_Crit_M006}`

### Requirement: The raw quests are validated at load

The catalog loader SHALL fail startup, naming the dataset, when the raw `mythic-quests` source is missing or
empty; when a record's `unitId` is not a catalog character; when a character's quest numbers are not exactly
1 to 5; when a `characterId` is blank; when a quest has no task or more than four, or a task has a `target` below 1; or when a reward string matches none of the
patterns above, names an `equipmentId` that is not a catalog equipment id, or names a `MythicShards` `unitId`
that is not a catalog character. A task's `heroId` parameter SHALL NOT be validated against the catalog: V1
names summons and NPCs there (`votanNpc2Steeljack`, `astarNpc1HaywireMine`, `tauSmnDroneSniper`), and one of
them is not in the catalog.

#### Scenario: Unknown reward string

- **GIVEN** a raw quest with the reward `blueStarMythic:1`
- **WHEN** the catalog loads
- **THEN** loading fails with a validation error naming `mythic-quests`

#### Scenario: Summon as the task hero

- **GIVEN** the shipped raw source, whose Marksman quest 1 task has `heroId: tauSmnDroneSniper`
- **WHEN** the catalog loads
- **THEN** loading succeeds and the parameter is served unchanged

#### Scenario: A valid source loads

- **GIVEN** the shipped raw source
- **WHEN** the catalog loads
- **THEN** loading succeeds and the dataset is served
