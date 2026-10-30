# api-error-contract Specification

## Purpose
Every error the API returns has one shape, so a client needs one parser and one `issueCode` switch instead of per-endpoint conflict bodies.

## Requirements

### Requirement: Errors are problem details with an issue code

Every writable 4xx/5xx response SHALL have `Content-Type: application/problem+json` and a body with `type`, `title`, `status`, `detail`, `instance`, `traceId` and `issueCode`. Validation failures SHALL additionally carry `errors`, a field-to-messages map. This contract applies before response headers start; no alternate error body shape SHALL be emitted while writable.

#### Scenario: Validation error

- **WHEN** a request fails field validation
- **THEN** the response is 400 `application/problem+json` with `issueCode` `validation` and `errors` naming each offending field

#### Scenario: Not found

- **WHEN** a request names a goal the caller does not own
- **THEN** the response is 404 `application/problem+json` with `issueCode` `notFound`

#### Scenario: Unhandled failure

- **WHEN** an endpoint throws an unexpected exception
- **THEN** the response is 500 `application/problem+json` with `issueCode` `internal` and a `traceId`, and no stack trace

### Requirement: Framework failures use the shared problem writer

Routing, authentication, and authorization failures SHALL use the same writer as endpoint failures: 404 `notFound`, 405 `methodNotAllowed`, 401 `unauthorized`, and 403 `forbidden`. Status-code and authentication handling SHALL retain required headers, including `WWW-Authenticate` and `Allow`.

#### Scenario: Framework-generated errors

- **WHEN** routing, authentication, or authorization produces 404, 405, 401, or 403 before headers start
- **THEN** the shared writer emits all problem fields and the matching issue code, retaining required headers

### Requirement: Started responses are not rewritten

After headers start, the API SHALL NOT append a problem document or rewrite status/headers. An unexpected failure SHALL be logged with its trace id, without exposing a stack trace, and the connection/stream SHALL be aborted.

#### Scenario: Failure after response headers started

- **GIVEN** response headers and some body bytes have already been sent
- **WHEN** an unexpected exception occurs
- **THEN** the API logs the trace id and aborts without appending a problem document

### Requirement: Conflicts carry the current state

Every 409 SHALL carry `issueCode` naming the conflict (`goalRevisionStale`, `goalOrderStale`, `slotOccupied`, `membershipStale`, `projectRevisionStale`, `legendaryEventPlanStale`, `legendaryEventOrderSetMismatch`, `tacticusUserIdInUse`, `concurrency`) and, where the conflicting resource is known, its current representation under `current` as its read endpoint returns, so the client can resynchronise without a second read.

#### Scenario: Stale goal revision

- **WHEN** a goal edit carries a stale `expectedRevision`
- **THEN** the response is 409 with `issueCode` `goalRevisionStale` and `current` holding the goal detail

#### Scenario: Stale order

- **WHEN** a reorder carries a stale `expectedOrderRevision`
- **THEN** the response is 409 with `issueCode` `goalOrderStale` and `current` holding `{ revision, goalIds }`

#### Scenario: Optimistic concurrency at save

- **WHEN** two writers update the same user settings and the second hits the concurrency token
- **THEN** the second receives 409 with `issueCode` `concurrency` and `current` holding the latest settings read representation `{ dailyEnergy, xpBookRarity, revision }`, re-read after the failed write

#### Scenario: Tacticus user id already linked

- **WHEN** a caller saves a Tacticus user id another profile holds
- **THEN** the response is 409 with `issueCode` `tacticusUserIdInUse` and no `current`

### Requirement: Rate-limit and upstream errors use the same shape

These errors SHALL use the problem-details shape, and their `detail` SHALL be safe to show to the user:

- 429 with `issueCode` `rateLimited` and `Retry-After`, from the per-profile limiter;
- 429 with `issueCode` `guildSyncCooldown` and `Retry-After`, from the per-guild sync cooldown;
- 502 with `issueCode` `upstreamRejected`;
- 503 with `issueCode` `upstreamUnavailable`.

#### Scenario: Upstream unavailable

- **WHEN** the Tacticus API times out during a player sync
- **THEN** the response is 503 `application/problem+json` with `issueCode` `upstreamUnavailable`

#### Scenario: Guild sync cooldown

- **WHEN** a guild is synchronized again within a minute of its last attempt
- **THEN** the response is 429 `application/problem+json` with `issueCode` `guildSyncCooldown` and a `Retry-After` header
