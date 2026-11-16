## MODIFIED Requirements

### Requirement: A roster snapshot stores the whole owned roster at one moment

A roster snapshot SHALL be a copy of the profile's whole owned roster stored inline on the record that took it, carrying `takenAt` (UTC), the `catalogVersion` at `takenAt`, `characters` and `mows`. `characters` SHALL list every owned character of the synced snapshot, ordered by `unitId`, each `{ unitId, progressionIndex, rank, xpLevel, shards, mythicShards, abilities: [{ abilityId, level }], appliedUpgradeSlots, equippedItems: [{ slotId, equipmentId, level }] }`; `mows` SHALL list every owned MoW, ordered by `unitId`, each `{ unitId, progressionIndex, xpLevel, shards, mythicShards, abilities: [{ abilityId, level }], appliedUpgradeSlots }`. Values SHALL be copied as synced. Each snapshot SHALL be a full roster, never a diff. A snapshot has no identity of its own: it is removed with its owning record and the account. The owners SHALL be Legendary Event run records and personal roster snapshots (see `personal-roster-snapshots`), and both SHALL produce the roster with one shared copy routine, so an equal synced roster yields an equal snapshot in either owner.

#### Scenario: Snapshot copies the synced roster

- **GIVEN** the synced roster owns `ultraCalgar` (Diamond1, `Legendary:RedFiveStars`, XP level 45, active 40, passive 38) and one MoW, and does not own `ultraTigurius`
- **WHEN** a roster snapshot is taken
- **THEN** its `characters` include `ultraCalgar` with exactly those values, its `mows` include the MoW, and `ultraTigurius` is absent

#### Scenario: Snapshot is not limited to one event's units

- **GIVEN** the synced roster owns `tauFarsight`, whom Uthar's lanes do not allow
- **WHEN** a sync opens an Uthar run record
- **THEN** the record's roster still includes `tauFarsight`

#### Scenario: Both owners copy identically

- **GIVEN** one synced roster
- **WHEN** a run record is opened and a personal snapshot is taken from it
- **THEN** the two rosters' `characters` and `mows` are equal

#### Scenario: Account purge removes snapshots

- **WHEN** an account with run records or personal snapshots is deleted
- **THEN** their rosters are removed with them
