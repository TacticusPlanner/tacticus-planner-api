## Purpose

Provides the backend game catalog's consolidated raid-boss reference data — the raid bosses and raid-boss primes, their stat-progression ladders, weapons, ability and trait ids, and the per-encounter modifiers that scale each boss within a guild-raid season — so a client can render a raid-boss library and per-boss detail view without parsing the raw datamine or joining season configs against unit sets itself.

## Requirements

### Requirement: The catalog serves one consolidated `raid-bosses` dataset

The game catalog SHALL serve a dataset keyed `raid-bosses` built from authored raw source files under `Data/raid-bosses/`. It SHALL be the only raid-boss dataset exposed publicly; the raw per-file sources (season rotation, unit sets, season configs, modifier definitions) SHALL NOT be served directly. The served dataset SHALL be self-contained: an encounter's boss/prime stats, weapons, abilities, traits, and scaled modifier definitions are inlined so the client never joins one served collection against another.

#### Scenario: Raid-bosses dataset is served without authentication

- **WHEN** a client requests the `raid-bosses` endpoint
- **THEN** the data is returned without requiring authentication, consistent with the rest of the game catalog

#### Scenario: Raid-bosses dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `raid-bosses`

#### Scenario: Raw sources have no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving a raw raid-boss source file (season rotation, unit sets, season configs, or modifier definitions) directly

### Requirement: Raid-boss records carry no display text or icon

Consistent with every other served catalog dataset, no record anywhere in the `raid-bosses` payload SHALL include a display name, short name, portrait or icon path, icon id, or wiki link. Only structural/identity fields exist: unit-set ids, faction ids, ability ids, trait ids, npc ids, board ids, numeric stats, tier/set/encounter indices, and modifier `{ type, target, subtarget, amount }` values. The client resolves every boss, prime, ability, trait, faction, and npc name and image from its id.

#### Scenario: Served payload shape

- **WHEN** any record in the `raid-bosses` dataset is served
- **THEN** it contains only structural/identity fields and no display-text or icon field

### Requirement: Bosses and raid-boss primes are the two served unit kinds

The `raid-bosses` dataset SHALL expose two ordered collections: `bosses` and `primes`. Each entry is keyed by its raw `unitSet` id (e.g. `GuildBoss4Boss1OrksGhazghkull`) and carries a `kind` field of `boss` or `prime`. Classification SHALL be derived from the raw `unitSet` key pattern — a key matching `GuildBoss<n>Boss…` is a boss; a key matching `GuildBoss<n>(MiniBoss|Minion)<m>…` is a prime. Field npcs and loot objects referenced only by encounters SHALL NOT appear as top-level `bosses`/`primes` entries. `bosses` SHALL be ordered by boss number ascending; `primes` by `(boss number, prime index)` ascending.

#### Scenario: Boss classification

- **WHEN** the raw unit sets include a key `GuildBoss4Boss1OrksGhazghkull`
- **THEN** the served dataset lists it under `bosses` with `kind` = `boss`

#### Scenario: Prime classification

- **WHEN** the raw unit sets include a key `GuildBoss1MiniBoss1TyranWarriorLeviathan`
- **THEN** the served dataset lists it under `primes` with `kind` = `prime`

#### Scenario: Field npcs are not top-level entries

- **WHEN** a raw unit set is referenced by an encounter only as `npc1id` / `npc2id` and its key matches neither the boss nor the prime pattern
- **THEN** it does not appear as a top-level `bosses` or `primes` entry (it may still be referenced by `encounter.fieldNpcIds`)

### Requirement: Each unit exposes its full stat-progression ladder

Every `bosses` / `primes` entry SHALL carry a non-empty ordered `statProgression` array — one entry per progression step, in the raw source order. Each step SHALL carry `health`, `damage`, `fixedArmor`, `rank`, `starLevel`, `baseRarity`, `progressionIndex`, and `abilityLevel` as non-nullable values (a missing numeric in the source is written as `0`), plus `blockChance`, `blockDamage`, `critChance`, and `critDamage` when the source provides them. Each entry SHALL also carry its `factionId` and `movement`. The catalog build SHALL fail if any unit's `statProgression` is empty.

#### Scenario: Progression ladder round-trips in order

- **WHEN** a raw unit set lists 9 stat steps
- **THEN** the served entry's `statProgression` has 9 steps in the same order, each with the numeric stat block

#### Scenario: Empty progression fails the build

- **WHEN** a raw unit set has an empty `stats` array
- **THEN** the catalog build fails validation

### Requirement: Weapons, abilities, and traits are served as structured ids

Each `bosses` / `primes` entry SHALL carry, when the source provides them: an ordered `weapons` array of `{ hits, damageProfile, range? }` (`range` present only for a ranged weapon), and `activeAbilityIds`, `passiveAbilityIds`, `relicAbilityIds`, and `traitIds` as ordered string-id arrays. Ability and trait display text, icons, and level scaling are resolved client-side from those ids. The catalog SHALL NOT inline ability descriptions or trait text.

#### Scenario: Ranged vs melee weapon

- **WHEN** a raw weapon carries a `Range` value
- **THEN** the served weapon has a numeric `range`; a weapon with no `Range` in the source has no `range` field

#### Scenario: Ability ids pass through

- **WHEN** a raw unit set lists two active abilities
- **THEN** the served entry's `activeAbilityIds` has those two id strings in source order and no ability description text

### Requirement: Each unit exposes its quest-unit reference id

Each `bosses` / `primes` entry SHALL carry `questUnitId` — a single npc id string copied verbatim from the raw unit set's `questUnitId` — when the source provides it, and SHALL omit the field entirely when the source does not. It is a structural identity field only: the catalog SHALL NOT resolve it to a display name, portrait, or icon path. An absent `questUnitId` SHALL NOT fail the build; not every unit set defines one.

#### Scenario: Quest-unit id passes through when present

- **WHEN** a raw unit set carries `questUnitId: "tyranNpc3Termagant"`
- **THEN** the served entry has `questUnitId` equal to `"tyranNpc3Termagant"` and no npc name, portrait, or icon field alongside it

#### Scenario: Quest-unit id omitted when absent

- **WHEN** a raw unit set has no `questUnitId`
- **THEN** the served entry has no `questUnitId` field and the build still succeeds

### Requirement: The dataset carries the season-config rotation and its tier/set/encounter structure

The `raid-bosses` dataset SHALL expose the ordered `seasonConfigRotation` (the list of season-config ids in rotation order) and a `seasons` collection keyed by season-config id. Each season SHALL carry its ordered `tiers`; each tier its `tier` number and ordered `sets`; each set its `set` number, `chestId`, `guildXp`, and ordered `encounters`. Tier and set ordering SHALL match the raw source order.

#### Scenario: Rotation order is preserved

- **WHEN** the raw `guildBossSeasonConfigRotation` lists five season-config ids
- **THEN** the served `seasonConfigRotation` is those five ids in the same order, and `seasons` has an entry for each

#### Scenario: Tier/set nesting round-trips

- **WHEN** a raw season config has 6 tiers, the first with 4 sets
- **THEN** the served season has 6 tiers, the first with 4 sets, each set carrying its `chestId`, `guildXp`, and `encounters`

### Requirement: Each encounter references its boss/prime by unit-set id and progression index

Every served `encounter` SHALL carry `encounterIndex`, `encounterType` (`Boss` or `Crystal`), `boardId`, `maxNrOfTurns`, the referenced `unitSetId` (the raw `unitId` with its `:N` suffix stripped), and `progressionIndex` (the integer parsed from that `:N` suffix, defaulting to `1` when absent). It SHALL carry `bossType` when the source provides it, an ordered `fieldNpcIds` array built from the source `npc1id`/`npc2id`/`enemies` (each likewise stripped of its `:N` suffix), a `disallowedFactionIds` array when the source constrains factions, and an ordered `modifiers` array (see the next requirement). The catalog build SHALL fail if an encounter's `unitSetId` does not resolve to a served `bosses`/`primes` entry or a raw field-npc unit set.

#### Scenario: Unit reference and progression index are split out

- **WHEN** a raw encounter's `unitId` is `GuildBoss1Boss1TyranTervigonLeviathan:3`
- **THEN** the served encounter has `unitSetId` = `GuildBoss1Boss1TyranTervigonLeviathan` and `progressionIndex` = `3`

#### Scenario: Missing progression suffix defaults to 1

- **WHEN** a raw encounter's `unitId` has no `:N` suffix
- **THEN** the served encounter's `progressionIndex` is `1`

#### Scenario: Unresolvable unit reference fails the build

- **WHEN** an encounter references a `unitSetId` that matches no served boss/prime entry and no raw field-npc unit set
- **THEN** the catalog build fails validation

### Requirement: Encounter modifiers are served with their resolved definition inlined

Each entry in an `encounter.modifiers` array SHALL carry the `hpLost` threshold (percentage of boss HP lost at which the modifier activates) and the resolved modifier definition inlined: `modifierId`, `type`, `target`, `subtarget` (or `subtargets` when the source lists several), and `amount`. The client SHALL NOT need a separate modifier-definitions collection to interpret an encounter. The catalog build SHALL fail if an encounter modifier id does not resolve to a raw modifier definition.

#### Scenario: Modifier definition is inlined

- **WHEN** a raw encounter modifier entry is `{ hpLost: 25, modifier: "boss_debuff_movement_1" }` and `boss_debuff_movement_1` is defined as `{ type: "bossStatDecrease", target: "movement", amount: 1 }`
- **THEN** the served encounter modifier carries `hpLost` = `25`, `modifierId` = `boss_debuff_movement_1`, `type` = `bossStatDecrease`, `target` = `movement`, and `amount` = `1`

#### Scenario: Unresolvable modifier id fails the build

- **WHEN** an encounter references a modifier id with no matching raw modifier definition
- **THEN** the catalog build fails validation

### Requirement: Primarch primes are flagged

The raw data marks a subset of prime unit sets as primarchs. Every served `primes` entry whose unit-set id appears in the raw `primarchs` list SHALL carry `isPrimarch` = `true`; all other primes SHALL carry `isPrimarch` = `false`.

#### Scenario: Primarch flag is set

- **WHEN** the raw `primarchs` list contains `GuildBoss5Boss1DeathMortarion`
- **THEN** the served entry for that unit set carries `isPrimarch` = `true`

### Requirement: Client distinguishes dataset absent, empty, and stale

The served projection SHALL let a client tell three states apart, because they drive different UI: the `raid-bosses` dataset not yet present in the client's catalog store (feature unavailable), the dataset present with a non-empty `bosses`/`primes` collection (render the library), and a dataset hash mismatch against the manifest (stale — trigger a re-sync). The served dataset SHALL never be served empty: `ManifestValidation` requires `bosses` and `primes` both non-empty.

#### Scenario: Empty served dataset fails the build

- **WHEN** the denormalized `raid-bosses` payload has an empty `bosses` or empty `primes` collection
- **THEN** the catalog build fails validation, so an empty dataset is never published

#### Scenario: Hash mismatch is detectable

- **WHEN** the client's stored `raid-bosses` payload hash differs from the manifest's `raid-bosses` hash
- **THEN** the client can detect the difference from the manifest alone and re-download the dataset

### Requirement: Adding the raid-bosses dataset does not bump the schema version

Introducing `raid-bosses` SHALL be purely additive: no existing served dataset's shape changes, the catalog `SchemaVersion` is not incremented, and no database migration is involved. A client unaware of `raid-bosses` SHALL be unaffected. The catalog `GameVersion` SHALL record the in-game version the raid-boss data was extracted from.

#### Scenario: Existing datasets and schema version unchanged

- **WHEN** the `raid-bosses` dataset is added to the catalog
- **THEN** the catalog `SchemaVersion` is unchanged and every previously served dataset's payload shape is unchanged

#### Scenario: Game version recorded

- **WHEN** the raid-boss raw data is authored or refreshed from the V1 datamine
- **THEN** the catalog release metadata's `GameVersion` reflects the in-game version it was extracted from
