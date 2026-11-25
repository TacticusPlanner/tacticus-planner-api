## Purpose

Keeps a guild's ended Guild Raid seasons before Tacticus stops serving them, and reports which seasons are held without exposing any hit.

## ADDED Requirements

### Requirement: A successful raid refresh starts capture of ended seasons

After `POST /api/v1/guild/raid-status/refresh` persists an active season, the system SHALL capture in the background every season from 70 up to the one before the active season, and every older season with stored hits, that is neither `final` nor `unavailable`. The season list in the guild response SHALL NOT decide what is captured, because Tacticus serves far more seasons than it lists. The refresh response SHALL NOT wait for capture or change because of its outcome. No capture SHALL start when the observation has no active season. A refresh served from the cooldown, a failed refresh, and `GET` requests SHALL NOT start capture.

Seasons SHALL be planned once per active season number. Capture SHALL make no upstream request when they were planned for the active season and no season is pending.

#### Scenario: First refresh backfills every season

- **GIVEN** active season 114, a Tacticus list of 113 and 114, and none captured
- **WHEN** a refresh succeeds
- **THEN** the refresh responds without waiting, and seasons 70 to 113 are requested one at a time in ascending order

#### Scenario: Nothing is due

- **GIVEN** every past season is final and seasons were planned for the active season
- **WHEN** another refresh succeeds
- **THEN** capture makes no upstream request

#### Scenario: A new season starts

- **GIVEN** seasons 70 to 113 are final
- **WHEN** a refresh first observes active season 115
- **THEN** only season 114 is requested

#### Scenario: Gap between seasons

- **WHEN** a refresh observes no active season
- **THEN** no capture starts and no season becomes final

### Requirement: Capture is resumable and limited per guild

At most one capture run SHALL work on a guild at a time across all API instances. Each season SHALL be persisted on its own, so an interrupted run keeps completed seasons and a later refresh continues with the rest. After a run that fails or leaves seasons pending, the next run SHALL start no sooner than one hour after it ended. A run that is cut off SHALL stop blocking others ten minutes after it last started a season.

#### Scenario: Restart in the middle of a backfill

- **GIVEN** a process stops after capturing three of ten seasons
- **WHEN** a refresh succeeds more than ten minutes later
- **THEN** only the seven remaining seasons are requested

#### Scenario: Two instances

- **WHEN** refreshes for one guild succeed on two API instances at once
- **THEN** one capture run makes upstream requests and the other makes none

### Requirement: Capture verifies the guild and season of each response

Every run SHALL verify, before storing anything, that the stored key answers for the same Tacticus guild. Every stored past-season response SHALL carry the requested season number. Nothing SHALL be stored from a response that fails its check, and a season whose number does not match SHALL stay pending while the run continues.

Verification SHALL NOT change the guild's members or roles.

#### Scenario: The key now answers for another guild

- **GIVEN** the key's owner moved to another guild
- **WHEN** capture runs
- **THEN** the run ends and no season or hit of the other guild is stored

#### Scenario: Wrong season returned

- **WHEN** season 110 is requested and the response identifies season 111
- **THEN** nothing is stored from it and season 110 stays pending

### Requirement: Past hits are stored from their own data

A past hit SHALL be stored with the season, tier, set, encounter, unit set, rarity, HP, damage, times and units reported by Tacticus, whether or not its position or season config exists in the current catalog. `progressionIndex` SHALL be the number in the hit's own unit id suffix, or null when there is none. The upstream tier SHALL be stored unchanged.

An entry with no player id, no completion time, no unit id, a malformed unit id suffix, or negative HP SHALL be skipped and counted. It SHALL NOT prevent the other entries from being stored.

#### Scenario: The config changed after the season

- **GIVEN** a hit on `guildBossAvatarMortarion:3` at a position where today's config has another boss
- **WHEN** its season is captured
- **THEN** the hit is stored with that unit set and `progressionIndex` 3, and the season becomes final

#### Scenario: One malformed entry

- **WHEN** one of 1,200 entries has no completion time
- **THEN** 1,199 hits are stored and the season reports one skipped entry

### Requirement: Capture adds to stored hits without duplicating or deleting them

A captured hit SHALL match a stored hit of the same season when player, tier, set, encounter, encounter type, unit set, rarity, remaining and maximum HP, damage, damage type, start time and completion time are equal. A match SHALL NOT be stored again, even when the upstream config hash differs. Stored hits missing from the response SHALL be kept.

#### Scenario: Season was partly seen live

- **GIVEN** 900 hits stored during live refreshes
- **WHEN** capture returns 1,200 hits including those 900
- **THEN** the season holds 1,200 hits

#### Scenario: Config hash differs

- **WHEN** a captured hit differs from a stored hit only in its upstream config hash
- **THEN** one hit is stored

### Requirement: A season is final only after it ended

A past season SHALL become `final` only after a successful response for it, requested while a higher season number was active, has been stored. A response with no valid entries SHALL make the season final only when no hits were stored for it and no entries were skipped. Final seasons SHALL NOT be requested again.

Capture SHALL NOT change the guild's live observation, its active season, or its refresh cooldown.

#### Scenario: Live status is untouched

- **GIVEN** season 114 is shown as current
- **WHEN** season 109 is captured
- **THEN** `GET guild/raid-status` returns the same body as before

#### Scenario: Suspicious empty response

- **GIVEN** 900 stored hits for season 113
- **WHEN** Tacticus returns season 113 with no entries
- **THEN** the hits are kept and the season stays pending

### Requirement: Capture failures are retried and lost seasons are reported

A rejected key (400, 401 or 403), a timeout or a server error SHALL end the run and leave its seasons pending. A season whose successful response cannot be read SHALL store nothing and stay pending while the run continues with the next. A 404 SHALL leave its season pending and continue with the next; its third 404 SHALL make it `unavailable` permanently.

A season missing from the Tacticus list SHALL NOT become `unavailable` for that reason. Stored hits SHALL be kept in every case. Credentials and full Tacticus user ids SHALL NOT be logged.

#### Scenario: Rejected key during backfill

- **WHEN** Tacticus answers 403 for season 106
- **THEN** no further season is requested in that run and the seasons captured before it stay final

#### Scenario: One season cannot be read

- **GIVEN** seasons 112 and 113 are pending
- **WHEN** the response for 112 holds an unknown rarity
- **THEN** 112 stays pending without hits from it, and 113 is captured in the same run

#### Scenario: Season is gone

- **GIVEN** season 103 is pending with 400 hits seen live
- **WHEN** Tacticus answers 404 for it a third time
- **THEN** season 103 becomes unavailable and keeps its 400 hits

### Requirement: Stored hits keep a partial player id

Every newly stored hit, live or captured, SHALL keep the last four characters of the player's Tacticus user id. A stored hit without them SHALL receive them when the same hit is captured or refreshed again. The full id SHALL NOT be stored with a hit. The keyed hash and the four characters SHALL NOT be returned by the season status read.

#### Scenario: Former player stays recognizable

- **WHEN** a hit by a player no longer in the guild is captured
- **THEN** it is stored with the keyed hash and the last four characters of that player's id

### Requirement: Captured hits have no expiry

Captured hits SHALL be kept until the guild's registration is deleted, which SHALL delete its seasons, hits and capture records. Deleting a Planner account SHALL NOT delete hits.

#### Scenario: Guild registration is deleted

- **WHEN** `DELETE guild` succeeds
- **THEN** that guild's seasons, hits and capture records are deleted

### Requirement: Season capture status can be read

`GET /api/v1/guild/raid/seasons` SHALL be authenticated, make no upstream call, and return for the caller's linked guild:

- `currentSeasonNumber`: integer, or null when no active season is persisted;
- `seasons`: array in descending season number, each with:
  - `seasonNumber`: positive integer;
  - `seasonConfigId`: string, or null when no response for the season was stored;
  - `status`: `live`, `pending`, `final` or `unavailable`;
  - `hasHits`: boolean;
  - `skippedEntryCount`: non-negative integer;
  - `observedAt`: UTC timestamp of the last stored response, or null;
  - `finalizedAt`: UTC timestamp, or null unless final.

The persisted active season SHALL be `live`. Every other season with a capture record or stored data SHALL appear with its capture status, and as `pending` when none is recorded. The response SHALL contain no hit, player id, name or credential. Season labels are derived by the client.

An unprovisioned profile SHALL receive 404. A caller without a linked registered guild SHALL receive 409 with `issueCode` `guildRaidNotReady`. A linked guild with no seasons SHALL receive 200 with an empty array.

#### Scenario: Mixed statuses

- **GIVEN** season 114 is live, 113 is final and 112 is pending
- **WHEN** a linked member reads the seasons
- **THEN** they are returned in that order with those statuses

#### Scenario: Plain member

- **WHEN** a linked member with role Member reads the seasons
- **THEN** the response is 200 and contains no player data

#### Scenario: No guild

- **WHEN** a caller without a linked guild reads the seasons
- **THEN** the response is 409 `guildRaidNotReady`
