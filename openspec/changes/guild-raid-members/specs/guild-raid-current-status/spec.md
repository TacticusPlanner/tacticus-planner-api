## MODIFIED Requirements

### Requirement: Reading status never calls upstream; forced refresh is a separate cooldown-gated endpoint

`GET /api/v1/guild/raid-status` SHALL always return the latest persisted observation for the caller's guild and SHALL NOT call the upstream Guild Raid API, regardless of how old that observation is.

`POST /api/v1/guild/raid-status/refresh` SHALL perform the upstream call, persist a successful result, and return the same discriminated response. It SHALL be gated by a one-minute per-guild cooldown measured from the guild's last sync attempt, successful or failed; a request inside that window SHALL NOT call upstream and SHALL return the current persisted result instead of an error, unless no successful observation has ever been persisted for the guild, in which case it SHALL return a service-unavailable response without calling upstream. Concurrent refresh requests for the same guild within one API instance SHALL share one upstream operation regardless of the cooldown.

After persisting a successful observation, that operation MAY also read the season that ended (see below) and refresh the roster (see `guild-member-verification`). Their outcome SHALL NOT change the refresh response, and a failure of either SHALL NOT fail the refresh.

#### Scenario: Read never triggers a sync

- **WHEN** any `GET` request arrives, fresh or long-stale
- **THEN** the response is served from the latest persisted observation and no upstream call is made

#### Scenario: Refresh within the cooldown reuses the last attempt

- **WHEN** a `POST /refresh` request arrives less than one minute after the guild's last sync attempt
- **THEN** the endpoint returns the current persisted result without calling upstream

#### Scenario: Refresh within the cooldown has no persisted result to reuse

- **WHEN** a `POST /refresh` request arrives less than one minute after the guild's last sync attempt and no successful observation has ever been persisted for the guild
- **THEN** the endpoint returns a service-unavailable response without calling upstream

#### Scenario: Manual and automatic refresh overlap

- **WHEN** multiple `POST /refresh` requests for the same guild require an upstream call concurrently
- **THEN** exactly one upstream current-status request runs and every caller receives its result

#### Scenario: Reading the ended season fails

- **WHEN** a refresh succeeds and the read of the season that ended answers 503
- **THEN** the refresh still returns its successful result

## ADDED Requirements

### Requirement: The season that ended is read once more

When a successful refresh persists an active season higher than the season the guild last observed as active, that earlier season SHALL become the guild's ended season, replacing any previous one. While an ended season is set, a successful refresh that persisted an active season SHALL request it by number once, with the stored key, at most once per hour per guild. Only the ended season SHALL be requested; seasons the guild never observed as active SHALL NOT be.

The ended season SHALL be cleared, and its season marked final, when a response for it carrying its own season number has been stored with at least one valid entry, or with none when no hits were stored for it and no entries were skipped. A third 404 for it SHALL clear it without marking it final. Any other failure SHALL leave it set. Stored hits SHALL be kept in every case.

Reading the ended season SHALL NOT change the guild's live observation, active season or refresh cooldown.

#### Scenario: First refresh of a new season

- **GIVEN** a guild that last observed season 113 as active
- **WHEN** a refresh first observes active season 114
- **THEN** season 113 is requested once after the live status is stored, and the season becomes final

#### Scenario: Gap between seasons

- **GIVEN** a guild that last observed season 113 as active
- **WHEN** a refresh observes no active season
- **THEN** season 113 is not requested yet

#### Scenario: Several seasons missed

- **GIVEN** a guild that last observed season 110 as active
- **WHEN** a refresh first observes active season 114
- **THEN** only season 110 is requested

#### Scenario: Retry after a failure

- **GIVEN** the read of season 113 timed out 20 minutes ago
- **WHEN** another refresh succeeds
- **THEN** season 113 is not requested again until an hour has passed

#### Scenario: Wrong season returned

- **WHEN** season 113 is requested and the response identifies season 114
- **THEN** nothing is stored from it and season 113 stays the ended season

#### Scenario: Suspicious empty response

- **GIVEN** 900 stored hits for season 113
- **WHEN** Tacticus returns season 113 with no entries
- **THEN** the hits are kept and season 113 stays the ended season

### Requirement: Ended-season hits are stored from their own data

A hit read from an ended season SHALL be stored with the season, tier, set, encounter, unit set, rarity, HP, damage, times and units reported by Tacticus, whether or not its position or season config exists in the current catalog. `progressionIndex` SHALL be the number in the hit's own unit id suffix, or null when there is none. The upstream tier SHALL be stored unchanged.

An entry with no player id, no completion time, no unit id, no unit before its suffix, a malformed unit id suffix, or negative HP SHALL be skipped and counted in the log. It SHALL NOT prevent the other entries from being stored. A response that cannot be read at all SHALL store nothing.

A hit read from an ended season SHALL match a stored hit of the same season when player, tier, set, encounter, encounter type, unit set, rarity, remaining and maximum HP, damage, damage type, start time and completion time are equal. A match SHALL NOT be stored again, even when the upstream config hash differs. Stored hits missing from the response SHALL be kept.

Live refresh SHALL keep rejecting a response with a hit outside today's catalog.

#### Scenario: The config changed during the season

- **GIVEN** a hit on `guildBossAvatarMortarion:3` at a position where today's config has another boss
- **WHEN** its ended season is read
- **THEN** the hit is stored with that unit set and `progressionIndex` 3

#### Scenario: Season was partly seen live

- **GIVEN** 900 hits stored during live refreshes
- **WHEN** the ended season returns 1,200 hits including those 900
- **THEN** the season holds 1,200 hits

#### Scenario: One malformed entry

- **WHEN** one of 1,200 entries has no completion time
- **THEN** 1,199 hits are stored

### Requirement: Stored hits keep a partial player id

Every newly stored hit SHALL keep the last four characters of the player's Tacticus user id. A stored hit without them SHALL receive them when the same hit is stored again. The full id SHALL NOT be stored with a hit. Stored hits SHALL be kept until the guild's registration is deleted; deleting a Planner account SHALL NOT delete them.

#### Scenario: Former player stays recognizable

- **WHEN** a hit by a player no longer in the guild is stored
- **THEN** it is stored with the keyed hash and the last four characters of that player's id
