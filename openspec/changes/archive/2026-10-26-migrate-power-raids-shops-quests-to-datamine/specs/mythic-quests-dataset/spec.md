## MODIFIED Requirements

### Requirement: The catalog serves a `mythic-quests` dataset

The game catalog SHALL serve a dataset keyed `mythic-quests` as a plain array at
`GET /api/v1/game-catalog/mythic-quests`, without requiring authentication, in the same dataset envelope as the
other served datasets. It SHALL be built from the quests named `mythic_{characterId}_{number}` in the `hero` group
of the pinned datamine copy `gameconfig/quests/groups.json`, keeping each quest's `rewards` and each task's
`locaKey`, `target` and `taskParameters`; a `mythic_` quest whose name does not fit that pattern SHALL fail
startup naming `mythic-quests`. The raw file SHALL NOT be served directly, and the catalog SHALL keep no
hand-kept mythic-quest file. Task text templates SHALL NOT be part of the catalog: task text is display text
and stays with the client.

#### Scenario: Dataset is served without authentication

- **WHEN** a client requests the `mythic-quests` endpoint
- **THEN** the array is returned without requiring authentication, wrapped in the same envelope as the other
  served datasets

#### Scenario: Dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `mythic-quests`, and the manifest snapshot test covers it

#### Scenario: The raw source has no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving the raw `groups.json` directly

### Requirement: Served mythic quest shape

Each served record SHALL carry exactly these fields:

- `unitId` (string): the character's catalog id.
- `quests` (array): the character's quests, ordered by ascending `number`. Each quest SHALL carry exactly:
  - `number` (integer, 1 to 5).
  - `tasks` (array, 1 to 4 entries, in source order). Each task SHALL carry exactly `locaKey` (string, the
    task-text template key, for example `SlayEnemiesWithHeroToTraitInOneBattle_description`), `target`
    (integer, at least 1) and `parameters` (object of string to string, copied unchanged from the game's
    `taskParameters`, for example `{ "trait": "Emplacement", "heroId": "blackAbaddon", "killAmount": "8" }`).
  - `rewards` (array, in source order) of reward records, defined in the next requirement.

Records SHALL be ordered by `unitId` (ordinal). The game's quest `name` (`mythic_blackAbaddon_01`) and task `name` SHALL NOT be
served. The client derives the task text from `locaKey` and `parameters` using its
own templates; the server sends no display text.

#### Scenario: Abaddon's first quest

- **GIVEN** the game's Abaddon quest 1 (`mythic_blackAbaddon_01`): task `SlayEnemiesWithHeroToTraitInOneBattle_description`, target 1, parameters
  `trait: Emplacement, heroId: blackAbaddon, killAmount: 8`
- **WHEN** the dataset is served
- **THEN** the `blackAbaddon` record's first quest has `number: 1` and one task with that `locaKey`,
  `target: 1` and those three parameters as strings

#### Scenario: Coverage

- **WHEN** the dataset is served
- **THEN** it has 119 records (the datamine adds `lostaBile` and `orksWeirdboy` to V1's 117), each with exactly 5 quests numbered 1 to 5
