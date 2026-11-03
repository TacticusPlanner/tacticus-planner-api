## ADDED Requirements

### Requirement: The rosterSnapshots part rebuilds V1 snapshots from their diff chain

When the `rosterSnapshots` part is selected, the import SHALL read V1 `rosterSnapshots` (`{ base, diffs }`) and rebuild each snapshot in order: `base`, then `diffs[0]` applied to `base`, then `diffs[i]` applied to the result for `diffs[i-1]`, applying V1's diff resolution and legacy-star fixing. Entries with `deletedDateMillisUtc` set SHALL be resolved (later diffs depend on them) but SHALL NOT be imported, and each SHALL get outcome `skippedDeleted`. When the key is absent or has no `base`, the part SHALL report `Skipped` with code `missing_roster_snapshots`. When it cannot be read, the part SHALL report `Failed` with code `invalid_roster_snapshots` and nothing is created. When the part is not selected, it SHALL report `Skipped` with code `not_selected`.

#### Scenario: Trashed entry still feeds the chain

- **GIVEN** a V1 base "Oct" with Calgar at Diamond1, a trashed diff "Mid" raising him to Diamond2, and a live diff "Nov" raising his passive only
- **WHEN** the part is imported
- **THEN** "Oct" and "Nov" are imported, "Nov" has Calgar at Diamond2 with the raised passive, and "Mid" gets `skippedDeleted`

#### Scenario: Not selected

- **WHEN** the import runs without `rosterSnapshots` selected
- **THEN** the part reports `Skipped` with code `not_selected` and no snapshot is created

### Requirement: V1 units are mapped onto the V2 roster shape

Each imported snapshot SHALL be stored as a personal snapshot with `source` `V1Import`, `takenAt` equal to V1 `dateMillisUtc`, `catalogVersion` `v1-import`, and the V1 name trimmed and cut to 60 characters (a blank name gets the default date name). For each unit:

- the V1 id SHALL become `unitId`;
- rarity and stars SHALL become `progressionIndex`;
- the V1 rank SHALL become `rank`;
- active/passive (characters) and primary/secondary (MoWs) levels SHALL become `abilities`, under the catalog unit's first ability ids of those kinds;
- `equip0..2` with their levels SHALL become `equippedItems` in the catalog slot matching each item's type;
- `xpLevel`, `shards` and `mythicShards` SHALL be copied.

Locked characters (rank `Locked`) and locked MoWs SHALL be omitted. Units and items whose ids the catalog does not know SHALL be omitted and counted in the outcome's `droppedUnits`. `xp` SHALL be stored as 0 and `appliedUpgradeSlots` as empty. For a `V1Import` snapshot both SHALL mean unknown, never zero progress.

#### Scenario: Worked example, Calgar

- **GIVEN** a V1 snapshot unit `{ id: "ultraCalgar", rank: Diamond1, rarity: Legendary, stars: RedFiveStars, activeAbilityLevel: 40, passiveAbilityLevel: 38, xpLevel: 45, shards: 320, mythicShards: 0 }`
- **WHEN** it is imported
- **THEN** the roster has `ultraCalgar` with rank Diamond1, progression `Legendary:RedFiveStars`, abilities [active 40, passive 38], XP level 45, shards 320, `xp` 0 and no applied upgrade slots

#### Scenario: Locked units are dropped

- **GIVEN** a V1 snapshot with `ultraTigurius` at rank `Locked` and a MoW with `locked: true`
- **WHEN** it is imported
- **THEN** neither unit is in the roster

### Requirement: Imported snapshots respect the manual limit, newest first

Live V1 snapshots SHALL be considered newest `dateMillisUtc` first. A snapshot that duplicates an existing `V1Import` snapshot (same name and `takenAt`) SHALL get `skippedDuplicate` and SHALL NOT use room. The others SHALL be imported while the profile's `Manual` + `V1Import` count is below the manual limit (see `personal-roster-snapshots`). The remaining ones SHALL get `skippedLimit`. Every source V1 snapshot SHALL get exactly one outcome `{ name, takenAt, status, code, droppedUnits }`, with status `imported`, `skippedDeleted`, `skippedDuplicate`, `skippedLimit` or `failed`. A failure on one snapshot SHALL NOT discard the others.

#### Scenario: Room for two

- **GIVEN** the profile has 18 manual snapshots and V1 has 5 live snapshots
- **WHEN** the part is imported
- **THEN** the two newest are imported and the other three get `skippedLimit`

#### Scenario: Re-running the import

- **GIVEN** a previous run imported all of a profile's V1 snapshots
- **WHEN** the part is imported again
- **THEN** every V1 snapshot gets `skippedDuplicate` and the count is unchanged
