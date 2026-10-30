## MODIFIED Requirements

### Requirement: Upstream-calling endpoints are limited per profile

The system SHALL limit `POST tacticus-api-key/validate` and `PUT account/tacticus-integration` to 10 requests/minute by default (`upstream-fast`). `POST tacticus-integration/player-sync`, `POST guild/register`, `POST guild/sync`, `DELETE guild`, and `POST account/v1-import` SHALL default to 6 requests/hour (`upstream-slow`). Limits SHALL be configurable and per profile. Other endpoints SHALL NOT be limited.

#### Scenario: Within the limit

- **WHEN** a profile issues its first `POST tacticus-integration/player-sync` in an hour
- **THEN** the request is processed normally

#### Scenario: Limit exceeded

- **GIVEN** a profile has issued 6 `POST guild/sync` requests within the current hour window
- **WHEN** it issues a seventh
- **THEN** the response is `429 Too Many Requests` with a `Retry-After` header and no upstream call is made

#### Scenario: Limits are per profile

- **GIVEN** profile A has exhausted its `player-sync` limit
- **WHEN** profile B issues `POST tacticus-integration/player-sync`
- **THEN** profile B's request is processed normally

#### Scenario: Unlimited endpoint

- **WHEN** a profile issues 100 `GET goals` requests in a minute
- **THEN** every request is processed normally

#### Scenario: Guild purge is limited

- **GIVEN** a profile has issued 6 `DELETE guild` requests within the current hour window
- **WHEN** it issues a seventh
- **THEN** the response is 429 `rateLimited` and no upstream call is made
