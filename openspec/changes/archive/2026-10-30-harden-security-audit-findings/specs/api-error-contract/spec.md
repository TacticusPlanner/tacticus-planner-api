## MODIFIED Requirements

### Requirement: Conflicts carry the current state

Every 409 SHALL carry `issueCode` naming the conflict (`goalRevisionStale`, `goalOrderStale`, `slotOccupied`, `membershipStale`, `projectRevisionStale`, `legendaryEventPlanStale`, `legendaryEventOrderSetMismatch`, `tacticusUserIdInUse`, `concurrency`) and, where the conflicting resource is known, its current representation under `current` in the same shape the resource's read endpoint returns, so the client can resynchronise without a second read. `tacticusUserIdInUse` carries no `current`, because the conflicting resource is another user's profile.

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
