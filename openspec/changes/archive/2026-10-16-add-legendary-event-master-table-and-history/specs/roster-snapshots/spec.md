# Spec Delta

## ADDED Requirements

### Requirement: A roster snapshot stores the whole owned roster at one moment

The system SHALL persist roster snapshots per profile. A snapshot SHALL carry an `id`, a `source`, an optional `name` (at most 60 characters), `takenAt` (UTC), the `catalogVersion` at `takenAt`, `characters` and `mows`. `characters` SHALL list every owned character of the synced snapshot, ordered by `unitId`, each `{ unitId, progressionIndex, rank, xpLevel, shards, mythicShards, abilities: [{ abilityId, level }], appliedUpgradeSlots, equippedItems: [{ slotId, equipmentId, level }] }`; `mows` SHALL list every owned MoW, ordered by `unitId`, each `{ unitId, progressionIndex, xpLevel, shards, mythicShards, abilities: [{ abilityId, level }], appliedUpgradeSlots }`. Values SHALL be copied as synced. Each snapshot SHALL be a full roster, never a diff against another snapshot. Snapshots SHALL be scoped to the caller's profile and removed with the account. The only `source` in this change SHALL be `legendaryEventRun`, with `name` null; other sources are reserved for the later port of V1's roster snapshots feature.

#### Scenario: Snapshot copies the synced roster

- **GIVEN** the synced roster owns `ultraCalgar` (Diamond1, `Legendary:RedFiveStars`, XP level 45, active 40, passive 38) and one MoW, and does not own `ultraTigurius`
- **WHEN** a roster snapshot is taken
- **THEN** its `characters` include `ultraCalgar` with exactly those values, its `mows` include the MoW, and `ultraTigurius` is absent

#### Scenario: Snapshot is not limited to one event's units

- **GIVEN** the synced roster owns `tauFarsight`, whom Uthar's lanes do not allow
- **WHEN** a sync opens an Uthar run record
- **THEN** the record's roster snapshot still includes `tauFarsight`

#### Scenario: Account purge removes snapshots

- **WHEN** an account with roster snapshots is deleted
- **THEN** its snapshots are removed

### Requirement: Run records own and rewrite their roster snapshot

A run record SHALL reference at most one roster snapshot and a `legendaryEventRun` snapshot SHALL belong to exactly one run record. Opening a run record SHALL insert a new `legendaryEventRun` snapshot from the incoming synced roster. Refreshing a run record SHALL rewrite its existing snapshot in place (`takenAt`, `catalogVersion`, `characters`, `mows`) and SHALL NOT insert another. A sync that leaves a run record unchanged SHALL leave its snapshot unchanged. If a snapshot is removed, the run record SHALL keep its other data with no snapshot. No roster-snapshot endpoint SHALL exist in this change; the snapshot is read only through the run-records response.

#### Scenario: Refresh rewrites, never appends

- **GIVEN** an Uthar run 2 record with snapshot S taken at 2026-10-11T08:00Z
- **WHEN** a sync at 2026-10-12T19:00Z changes Uthar's lanes
- **THEN** the record still references S, S has `takenAt` 2026-10-12T19:00Z and the current roster, and the profile has no other snapshot for that record

#### Scenario: Frozen progress keeps the snapshot

- **GIVEN** the same record, and a later sync with Uthar's encounters unchanged after the player ranked up Calgar
- **WHEN** the sync completes
- **THEN** S still holds Calgar at his earlier rank
