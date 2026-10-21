## MODIFIED Requirements

### Requirement: A roster snapshot stores the whole owned roster at one moment

A roster snapshot SHALL be a copy of the profile's whole owned roster stored inline on the record that took it, carrying `takenAt` (UTC), the `catalogVersion` at `takenAt`, `characters` and `mows`. `characters` SHALL list every owned character of the synced snapshot, ordered by `unitId`, each `{ unitId, progressionIndex, rank, xpLevel, shards, mythicShards, abilities: [{ abilityId, level }], appliedUpgradeSlots, equippedItems: [{ slotId, equipmentId, level }] }`; `mows` SHALL list every owned MoW, ordered by `unitId`, each `{ unitId, progressionIndex, xpLevel, shards, mythicShards, abilities: [{ abilityId, level }], appliedUpgradeSlots }`. Values SHALL be copied as synced. Each snapshot SHALL be a full roster, never a diff. A snapshot has no identity of its own: it is removed with its owning record and the account. Legendary Event run records are the only owner.

#### Scenario: Snapshot copies the synced roster

- **GIVEN** the synced roster owns `ultraCalgar` (Diamond1, `Legendary:RedFiveStars`, XP level 45, active 40, passive 38) and one MoW, and does not own `ultraTigurius`
- **WHEN** a roster snapshot is taken
- **THEN** its `characters` include `ultraCalgar` with exactly those values, its `mows` include the MoW, and `ultraTigurius` is absent

#### Scenario: Snapshot is not limited to one event's units

- **GIVEN** the synced roster owns `tauFarsight`, whom Uthar's lanes do not allow
- **WHEN** a sync opens an Uthar run record
- **THEN** the record's roster still includes `tauFarsight`

#### Scenario: Account purge removes snapshots

- **WHEN** an account with run records is deleted
- **THEN** their rosters are removed with them

### Requirement: Run records own and rewrite their roster snapshot

Every run record SHALL carry exactly one roster snapshot. Opening a run record SHALL take the snapshot from the incoming synced roster. Refreshing a run record SHALL rewrite its snapshot in place (`takenAt`, `catalogVersion`, `characters`, `mows`). A sync that leaves a run record unchanged SHALL leave its snapshot unchanged. The snapshot SHALL be readable only through the run-record roster read (see `legendary-event-run-records`).

#### Scenario: Refresh rewrites, never appends

- **GIVEN** an Uthar run 2 record whose roster was taken at 2026-10-11T08:00Z
- **WHEN** a sync at 2026-10-12T19:00Z changes Uthar's lanes
- **THEN** the record's roster has `takenAt` 2026-10-12T19:00Z and the current roster

#### Scenario: Frozen progress keeps the snapshot

- **GIVEN** the same record, and a later sync with Uthar's encounters unchanged after the player ranked up Calgar
- **WHEN** the sync completes
- **THEN** the record's roster still holds Calgar at his earlier rank
