# upstream-call-rate-limiting Specification

## Purpose

Endpoints that call the upstream Tacticus or V1 APIs on the caller's behalf are limited per profile so one client cannot exhaust the shared upstream quota or amplify traffic.

## Requirements

### Requirement: Upstream-calling endpoints are limited per profile

The system SHALL limit `POST tacticus-api-key/validate` and `PUT account/tacticus-integration` to 10 requests/minute by default (`upstream-fast`). `POST tacticus-integration/player-sync`, `POST guild/register`, `POST guild/sync`, and `POST account/v1-import` SHALL default to 6 requests/hour (`upstream-slow`). Limits SHALL be configurable and per profile. Other endpoints SHALL NOT be limited.

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

### Requirement: Each endpoint has its own fixed-window bucket

Buckets SHALL be keyed by `(profileId, HTTP method, canonical endpoint)`; use `(issuer, subject)` for identity before profile provisioning. Endpoints sharing a policy SHALL NOT share quota. Windows SHALL be fixed, non-overlapping, and anchored at the bucket's first accepted request, with no carry-over permits or queued requests.

#### Scenario: Endpoints do not share quota

- **GIVEN** a profile has exhausted key validation and guild sync
- **WHEN** it calls integration update or player sync with unused quota
- **THEN** each request is evaluated against its own endpoint bucket

### Requirement: Retry-After describes the rejecting window

A 429 SHALL carry `Retry-After` as integer delay-seconds `max(1, ceil((windowEndUtc - nowUtc).TotalSeconds))` for the rejecting window. At the exact reset boundary, the renewed window SHALL be evaluated before deciding whether to reject.

#### Scenario: Retry delay and the reset boundary

- **GIVEN** an exhausted bucket whose window ends at T
- **WHEN** it rejects a request at T minus 1.2 seconds
- **THEN** `Retry-After` is `2`, and a request at T can consume a renewed permit

### Requirement: Rate-limit responses are not counted as upstream attempts

A request rejected with 429 SHALL NOT update any "last attempted" or "last succeeded" timestamp, SHALL NOT change the raid-refresh cooldown, and SHALL NOT be recorded as an analytics sync event.

#### Scenario: Rejected sync leaves state untouched

- **GIVEN** a profile whose `player-sync` limit is exhausted and whose last sync attempt was at T
- **WHEN** it issues another `POST tacticus-integration/player-sync`
- **THEN** the response is 429 and the stored last-attempt time remains T
