## Purpose

Endpoints that call the upstream Tacticus or V1 APIs on the caller's behalf are limited per profile so one client cannot exhaust the shared upstream quota or amplify traffic.

## ADDED Requirements

### Requirement: Upstream-calling endpoints are limited per profile

The system SHALL apply a per-profile request limit to `POST tacticus-api-key/validate`, `PUT me/tacticus-integration`, `POST tacticus-integration/player-sync`, `POST guilds/register`, `POST guilds/me/sync` and `POST me/v1-import`. Limits SHALL be configurable; defaults are 10 requests per minute for key validation and integration updates, and 6 requests per hour for player sync, guild register, guild sync and V1 import. Other endpoints SHALL NOT be limited.

#### Scenario: Within the limit

- **WHEN** a profile issues its first `POST tacticus-integration/player-sync` in an hour
- **THEN** the request is processed normally

#### Scenario: Limit exceeded

- **GIVEN** a profile has issued 6 `POST guilds/me/sync` requests within the current hour window
- **WHEN** it issues a seventh
- **THEN** the response is `429 Too Many Requests` with a `Retry-After` header and no upstream call is made

#### Scenario: Limits are per profile

- **GIVEN** profile A has exhausted its `player-sync` limit
- **WHEN** profile B issues `POST tacticus-integration/player-sync`
- **THEN** profile B's request is processed normally

#### Scenario: Unlimited endpoint

- **WHEN** a profile issues 100 `GET me/goals` requests in a minute
- **THEN** every request is processed normally

### Requirement: Rate-limit responses are not counted as upstream attempts

A request rejected with 429 SHALL NOT update any "last attempted" or "last succeeded" timestamp, SHALL NOT change the raid-refresh cooldown, and SHALL NOT be recorded as an analytics sync event.

#### Scenario: Rejected sync leaves state untouched

- **GIVEN** a profile whose `player-sync` limit is exhausted and whose last sync attempt was at T
- **WHEN** it issues another `POST tacticus-integration/player-sync`
- **THEN** the response is 429 and the stored last-attempt time remains T
