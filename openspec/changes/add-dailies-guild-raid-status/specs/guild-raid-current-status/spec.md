## Purpose

Provides an authenticated, normalized view of a registered guild's current raid position so clients can show live boss context without consuming raw hit history.

## ADDED Requirements

### Requirement: Current Guild Raid status is scoped to a ready registered guild

The system SHALL expose an authenticated `GET /api/v1/guilds/me/raid-status` endpoint. It SHALL use the caller's linked guild and that guild's stored encrypted API token without returning the token or another member's hit history.

The endpoint SHALL return conflict status when the caller has no linked registered guild, the guild has never completed synchronization, or the guild has no usable stored token. It SHALL preserve the existing not-found behavior for an unprovisioned profile and SHALL NOT call the upstream Guild Raid API in any of these cases.

#### Scenario: Ready guild requests its status

- **WHEN** an authenticated caller belongs to a registered guild with a successful synchronization and usable stored token
- **THEN** the endpoint returns the normalized current Guild Raid status for that guild

#### Scenario: Guild access is not ready

- **WHEN** the caller is not linked to a registered and successfully synchronized guild
- **THEN** the endpoint returns a conflict response that identifies the missing prerequisite without exposing any guild credential

### Requirement: The endpoint returns a stable discriminated response

Every successful response SHALL contain:

- `state`: `active` or `noActiveSeason`;
- `observedAt`: UTC timestamp of the upstream observation represented by the payload;
- `freshness`: `fresh` or `stale`;
- `lastGuildSyncSucceededAt`: UTC timestamp from the registered guild;
- `season`: null for `noActiveSeason`, otherwise the active-season object.

An active-season object SHALL contain:

- `seasonNumber`: positive integer from the upstream response;
- `seasonConfigId`: stable id from the upstream response;
- `endsAt`: UTC timestamp when a matching explicitly-authored active event occurrence supplies one, otherwise null;
- `tierIndex`: zero-based non-negative catalog tier index;
- `setIndex`: zero-based non-negative set index within that tier;
- `setCount`: positive number of sets in the tier before any configured loop;
- `difficulty`: stable rarity string from the current boss observation/catalog progression;
- `boss`: current boss object;
- `nextBoss`: the next main-boss object, or null when catalog data defines no next position;
- `primes`: ordered array of zero or more current support encounters.

A boss object SHALL contain `unitSetId` (string), `progressionIndex` (positive integer), `remainingHp` (non-negative integer), `maximumHp` (positive integer), and `isUpcoming` (boolean). A next-boss object SHALL contain `unitSetId`, `progressionIndex`, `tierIndex`, and `setIndex`.

A prime object SHALL contain `encounterIndex`, `unitSetId`, `progressionIndex`, nullable `remainingHp` and `maximumHp`, plus an ordered `modifiers` array. Each modifier SHALL contain `modifierId`, `type`, `target`, nullable `subtarget`, numeric `amount`, `activationRemainingHp`, and `active`.

The server SHALL send stable ids and structured values only. The client SHALL derive names, icons, localized modifier descriptions, and display labels.

#### Scenario: Active season is normalized

- **WHEN** the upstream response identifies an active season and its config resolves in the raid-boss catalog
- **THEN** the response contains the current position, boss HP, next boss, prime state, modifier thresholds, observation time, and guild-sync time in the declared shape

#### Scenario: No season is active

- **WHEN** the upstream source reports that no Guild Raid season is active
- **THEN** the endpoint returns `state: noActiveSeason`, `season: null`, and freshness metadata rather than an error or an invented season

### Requirement: Current and next positions are derived from one canonical season sequence

The system SHALL resolve `seasonConfigId` against the served raid-boss catalog and use that config's ordered tiers, sets, encounters, and loop metadata as the canonical sequence. It SHALL select the latest main-boss observation by completion timestamp. A living latest boss is current; a defeated latest boss advances to the next configured position. When no main-boss hit exists, the first configured position is current and its HP SHALL come from the selected catalog progression step.

For a position revisited by a configured loop, observations completed before the most recent main-boss defeat SHALL NOT be reused as the current boss or prime HP. Current tier/set, next boss, full HP, and modifier thresholds SHALL all be derived from the same selected catalog position.

#### Scenario: Latest boss is still alive

- **WHEN** the latest main-boss observation has positive remaining HP
- **THEN** that observation's matching catalog position is current and its reported remaining/maximum HP are returned

#### Scenario: Latest boss was defeated

- **WHEN** the latest main-boss observation has zero remaining HP
- **THEN** the next configured position becomes current with `isUpcoming: true`, including configured loop behavior

#### Scenario: A loop revisits an earlier boss id

- **WHEN** the current configured position reuses a boss or prime unit-set id seen before the latest main-boss defeat
- **THEN** earlier-loop observations are ignored when calculating current HP and modifier progress

### Requirement: Modifier progress is calculated against current prime HP

For each current prime, the system SHALL scale the encounter's authored `hpLost` thresholds to that prime's resolved maximum HP using the same proportional rule as the raid-boss catalog. A modifier is active when resolved HP lost is at least its scaled threshold. `activationRemainingHp` SHALL equal maximum HP minus the scaled HP-lost threshold.

When current prime maximum HP cannot be resolved, its HP and derived modifier activation values SHALL be null rather than estimated from a different encounter.

#### Scenario: Prime crosses a modifier threshold

- **WHEN** a prime with 1,000,000 maximum HP has lost enough HP to pass a modifier's scaled 250,000 HP-lost threshold
- **THEN** the modifier is returned as active with `activationRemainingHp: 750000`

#### Scenario: Prime HP is unknown

- **WHEN** neither a current observation nor the selected catalog progression provides the prime's maximum HP
- **THEN** the prime remains present but its HP and HP-derived modifier values are null

### Requirement: Season timing uses explicit event occurrences only

Guild Raid seasons SHALL NOT be projected from a guessed recurrence. `endsAt` SHALL be populated only from an explicitly authored `GuildRaidSeason` occurrence matching the active observation; the catalog definition remains non-recurring and the endpoint considers only the current game-catalog projection window and version. If no matching occurrence exists, `endsAt` SHALL be null while all other active status remains available.

#### Scenario: Explicit season end is available

- **WHEN** the current catalog version contains an active Guild Raid occurrence matching the observed season
- **THEN** its end timestamp is returned as `endsAt`

#### Scenario: Season end is not known

- **WHEN** no explicit matching occurrence exists
- **THEN** `endsAt` is null and the server does not infer an anchor or countdown

### Requirement: Status refresh is cached and single-flight

The endpoint SHALL cache a successful normalized observation per guild as fresh for five minutes. A request without explicit refresh SHALL return that observation immediately while fresh and SHALL refresh it after it becomes stale. `refresh=true` SHALL bypass the fresh-age check. Concurrent refresh requests for the same guild SHALL share one upstream operation.

If refresh fails and a successful observation no more than 30 minutes old exists, the endpoint SHALL return it with `freshness: stale`. If no such observation exists, upstream rejection SHALL produce a bad-gateway response and transient unavailability or timeout SHALL produce a service-unavailable response. Failed/no-active observations SHALL NOT overwrite the latest successful active observation used for stale fallback.

#### Scenario: Fresh cached status is reused

- **WHEN** a non-forced request arrives less than five minutes after a successful observation
- **THEN** the cached response is returned without an upstream call

#### Scenario: Manual and automatic refresh overlap

- **WHEN** multiple requests for the same guild require refresh concurrently
- **THEN** exactly one upstream request runs and every caller receives its result

#### Scenario: Refresh fails with recent retained data

- **WHEN** refresh fails transiently and the guild has a successful observation no more than 30 minutes old
- **THEN** that observation is returned with `freshness: stale`

#### Scenario: Refresh fails without usable retained data

- **WHEN** refresh fails and no successful observation within the stale window exists
- **THEN** the endpoint returns the mapped upstream error and does not fabricate status
