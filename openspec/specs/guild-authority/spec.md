# guild-authority Specification

## Purpose
Who may sync or purge a registered guild, how the caller's Leader/Co-Leader role is verified against the live Tacticus roster, and how often a guild can be synced.

## Requirements

### Requirement: Guild purge is pre-gated without an upstream call

`DELETE guild` SHALL respond 403, with no upstream call and nothing deleted, when any of these is missing:

- a configured Tacticus user id;
- a link to a registered guild as a persisted Leader or Co-Leader;
- a stored Guild API token for that guild.

#### Scenario: Cleared Tacticus user id

- **GIVEN** a former Co-Leader who has cleared their Tacticus user id
- **WHEN** they send `DELETE guild`
- **THEN** the response is 403 and the guild still exists

#### Scenario: Plain member does not spend the token

- **GIVEN** a caller persisted as a plain member
- **WHEN** they send `DELETE guild`
- **THEN** the response is 403 and no upstream request is made

### Requirement: Guild purge re-verifies the caller against a fresh upstream roster

After the pre-gate, `DELETE guild` SHALL synchronize the guild with its stored token. It SHALL delete the registration, roster and raid history only when that sync succeeds and lists the caller as Leader or Co-Leader of the same guild. A persisted role SHALL never authorize the purge on its own.

#### Scenario: Current leader purges

- **GIVEN** a caller linked as Co-Leader whom the fresh upstream roster still lists as Co-Leader
- **WHEN** the caller sends `DELETE guild`
- **THEN** the response is 204 and the guild and its member rows are deleted

#### Scenario: Demoted upstream, not yet re-synced

- **GIVEN** a caller persisted as Co-Leader whom the upstream roster now lists as a plain member
- **WHEN** the caller sends `DELETE guild`
- **THEN** the response is 403 and the guild still exists

### Requirement: Guild purge fails closed

When the purge's synchronization does not succeed, nothing SHALL be deleted. The response SHALL be:

- 400 when Tacticus rejected the token or returned malformed data;
- 403 when the caller is not Leader or Co-Leader upstream, or the token now answers for a different guild;
- 409 on a uniqueness conflict while refreshing the roster;
- 503 when Tacticus is unavailable.

#### Scenario: Tacticus unavailable

- **GIVEN** a caller persisted as Leader while the Tacticus guild API times out
- **WHEN** they send `DELETE guild`
- **THEN** the response is 503 `upstreamUnavailable` and nothing is deleted

### Requirement: Guild sync is gated on the persisted role

`POST guild/sync` SHALL respond 403, with no upstream call, when the caller's persisted role is not Leader or Co-Leader. The fresh upstream roster SHALL remain the authority: a caller it no longer lists as Leader or Co-Leader SHALL be rejected, and nothing SHALL be persisted.

#### Scenario: Plain member is refused before upstream

- **GIVEN** a caller persisted as a plain member of a registered guild
- **WHEN** they send `POST guild/sync`
- **THEN** the response is 403 and no upstream request is made

### Requirement: Guild sync is cooled down per guild

`POST guild/sync` SHALL make at most one upstream roster fetch per guild per minute, whichever Leader or Co-Leader asks. The cooldown SHALL be claimed atomically before the upstream call, and a failed attempt SHALL count. While it is open, the response SHALL be 429 with `issueCode` `guildSyncCooldown` and `Retry-After` `max(1, ceil(remaining seconds))`.

#### Scenario: Second leader inside the window

- **GIVEN** the guild's Leader synchronized it 20 seconds ago
- **WHEN** its Co-Leader sends `POST guild/sync`
- **THEN** the response is 429 with `issueCode` `guildSyncCooldown` and `Retry-After` `40`, and no upstream request is made

#### Scenario: After the window

- **GIVEN** the guild's last sync attempt was more than one minute ago
- **WHEN** a current Leader sends `POST guild/sync`
- **THEN** the guild is synchronized and the response is 200

### Requirement: Guild reads may use the persisted role until the next sync

Guild reads SHALL be authorized from the persisted link and role without an upstream call. These reads are `GET guild`, `GET guild/raid-status` and the raid refresh. A member who has departed or been demoted upstream MAY keep this access until the next guild sync revokes it. A profile SHALL be linked to at most one guild.

#### Scenario: Departed member before the next sync

- **GIVEN** a member removed from the guild upstream, before any guild sync has run
- **WHEN** they send `GET guild`
- **THEN** the response is 200 with the guild as last synchronized
