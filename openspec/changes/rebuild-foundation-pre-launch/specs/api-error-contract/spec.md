## Purpose

Every error the API returns has one shape, so a client needs one parser and one `issueCode` switch instead of per-endpoint conflict bodies.

## ADDED Requirements

### Requirement: Errors are problem details with an issue code

Every non-2xx/3xx response SHALL have `Content-Type: application/problem+json` and a body with `type`, `title`, `status`, `detail`, `instance`, `traceId` and an `issueCode` string. Validation failures (400) SHALL additionally carry `errors`, a map of field name to messages. No other error body shape SHALL be returned by any endpoint, including unhandled exceptions (500, `issueCode` `internal`).

#### Scenario: Validation error

- **WHEN** a request fails field validation
- **THEN** the response is 400 `application/problem+json` with `issueCode` `validation` and `errors` naming each offending field

#### Scenario: Not found

- **WHEN** a request names a goal the caller does not own
- **THEN** the response is 404 `application/problem+json` with `issueCode` `notFound`

#### Scenario: Unhandled failure

- **WHEN** an endpoint throws an unexpected exception
- **THEN** the response is 500 `application/problem+json` with `issueCode` `internal` and a `traceId`, and no stack trace

### Requirement: Conflicts carry the current state

Every 409 SHALL carry `issueCode` naming the conflict (`goalRevisionStale`, `goalOrderStale`, `slotOccupied`, `membershipStale`, `projectRevisionStale`, `legendaryEventPlanStale`, `legendaryEventOrderSetMismatch`, `concurrency`) and, where the conflicting resource is known, its current representation under `current` in the same shape the resource's read endpoint returns, so the client can resynchronise without a second read.

#### Scenario: Stale goal revision

- **WHEN** a goal edit carries a stale `expectedRevision`
- **THEN** the response is 409 with `issueCode` `goalRevisionStale` and `current` holding the goal detail

#### Scenario: Stale order

- **WHEN** a reorder carries a stale `expectedOrderRevision`
- **THEN** the response is 409 with `issueCode` `goalOrderStale` and `current` holding `{ orderRevision, goalIds }`

#### Scenario: Optimistic concurrency at save

- **WHEN** two writers update the same user settings and the second hits the concurrency token
- **THEN** the second receives 409 with `issueCode` `concurrency`

### Requirement: Rate-limit and upstream errors use the same shape

429 (`issueCode` `rateLimited`, with `Retry-After`), 502 (`issueCode` `upstreamRejected`) and 503 (`issueCode` `upstreamUnavailable`) SHALL use the problem-details shape; `detail` SHALL be safe to show to the user.

#### Scenario: Upstream unavailable

- **WHEN** the Tacticus API times out during a player sync
- **THEN** the response is 503 `application/problem+json` with `issueCode` `upstreamUnavailable`
