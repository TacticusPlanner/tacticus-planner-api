## Purpose

Provides an authenticated, normalized view of a registered guild's current raid position so clients can show live boss context without consuming raw hit history.

## ADDED Requirements

### Requirement: Current Guild Raid status is scoped to a ready registered guild

The system SHALL expose an authenticated `GET /api/v1/guilds/me/raid-status` endpoint. It SHALL use the caller's linked guild and that guild's stored encrypted API token without returning the token or another member's hit history.

The endpoint SHALL return conflict status when the caller has no linked registered guild, the guild has never completed synchronization, the guild has no usable stored token, or the guild has never had a successful Guild Raid status observation persisted. It SHALL preserve the existing not-found behavior for an unprovisioned profile. `GET` SHALL NOT call the upstream Guild Raid API under any circumstance, including these prerequisite-failure cases and the never-observed case.

#### Scenario: Ready guild requests its status

- **WHEN** an authenticated caller belongs to a registered guild with a successful synchronization and usable stored token
- **THEN** the endpoint returns the normalized current Guild Raid status for that guild

#### Scenario: Guild access is not ready

- **WHEN** the caller is not linked to a registered and successfully synchronized guild
- **THEN** the endpoint returns a conflict response that identifies the missing prerequisite without exposing any guild credential

#### Scenario: Guild is ready but has no raid-status observation yet

- **WHEN** the caller's guild is registered, synchronized, and holds a usable token, but no Guild Raid status observation has ever been persisted for it
- **THEN** the endpoint returns a conflict response instead of performing an inline first upstream fetch

### Requirement: The endpoint returns a stable discriminated response

Both `GET /api/v1/guilds/me/raid-status` and `POST /api/v1/guilds/me/raid-status/refresh` SHALL return the same discriminated response on success. Every successful response SHALL contain:

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
- `primes`: ordered array of zero or more current support encounters.

A boss object SHALL contain `unitSetId` (string), `progressionIndex` (positive integer), `remainingHp` (non-negative integer), `maximumHp` (positive integer), and `isUpcoming` (boolean).

A prime object SHALL contain `encounterIndex`, `unitSetId`, `progressionIndex`, nullable integer `remainingHp` and `maximumHp`, plus an ordered `modifiers` array. Each modifier SHALL contain string `modifierId`, `type`, and `target`; nullable string `subtarget`; numeric `amount`; nullable integer `activationRemainingHp`; and nullable boolean `active`. `activationRemainingHp` and `active` SHALL both be null when the prime's maximum HP cannot be resolved.

The server SHALL send stable ids and structured values only. The client SHALL derive names, icons, localized modifier descriptions, and display labels.

#### Scenario: Active season is normalized

- **WHEN** the upstream response identifies an active season and its config resolves in the raid-boss catalog
- **THEN** the response contains the current position, boss HP, prime state, modifier thresholds, observation time, and guild-sync time in the declared shape

#### Scenario: No season is active

- **WHEN** the upstream source reports that no Guild Raid season is active
- **THEN** the endpoint returns `state: noActiveSeason`, `season: null`, and freshness metadata rather than an error or an invented season

### Requirement: The current position is derived from one canonical season sequence

The system SHALL resolve `seasonConfigId` against the served raid-boss catalog and use that config's ordered tiers, sets, encounters, and loop metadata as the canonical sequence. It SHALL select the latest main-boss observation by completion timestamp. A living latest boss is current; a defeated latest boss advances to the next configured position. When no main-boss hit exists, the first configured position is current and its HP SHALL come from the selected catalog progression step.

For a position revisited by a configured loop, observations completed before the most recent main-boss defeat SHALL NOT be reused as the current boss or prime HP. Current tier/set, full HP, and modifier thresholds SHALL all be derived from the same selected catalog position. The endpoint SHALL NOT calculate or return a preview of the subsequent boss.

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
- **THEN** the prime remains present with `remainingHp: null` and `maximumHp: null`, and every modifier has `activationRemainingHp: null` and `active: null`

### Requirement: Season timing uses explicit event occurrences only

Guild Raid seasons SHALL NOT be projected from a guessed recurrence. `endsAt` SHALL be populated only from an explicitly authored `GuildRaidSeason` occurrence matching the active observation; the catalog definition remains non-recurring and the endpoint considers only the current game-catalog projection window and version. If no matching occurrence exists, `endsAt` SHALL be null while all other active status remains available.

#### Scenario: Explicit season end is available

- **WHEN** the current catalog version contains an active Guild Raid occurrence matching the observed season
- **THEN** its end timestamp is returned as `endsAt`

#### Scenario: Season end is not known

- **WHEN** no explicit matching occurrence exists
- **THEN** `endsAt` is null and the server does not infer an anchor or countdown

### Requirement: Successful Guild Raid observations are retained

The system SHALL retain each successful normalized Guild Raid observation by registered guild and upstream season so the same current-status projection remains available after an API process restart. It SHALL NOT retain credentials or derived rankings, performance scores, token summaries, or team recommendations as part of the observation. Repeated or overlapping refreshes of the same logical observation SHALL NOT create duplicate season or hit facts.

#### Scenario: Successful refresh persists source facts

- **WHEN** the upstream source returns an active season for a ready guild
- **THEN** a subsequent status read can reproduce that guild's observation after an API process restart without duplicating its season or hits

#### Scenario: No-active observation is persisted

- **WHEN** the upstream source successfully reports that no season is active
- **THEN** a subsequent status read can reproduce the no-active result and observation time without losing previously retained season observations

#### Scenario: Repeated refresh is idempotent

- **WHEN** the same upstream season and hits are observed again or overlapping API instances complete refresh
- **THEN** uniqueness constraints prevent duplicate season and hit facts while preserving one reproducible observation

### Requirement: Current-user hit access is caller scoped

A server-side current-user hit read SHALL accept one guild raid season and the authenticated caller's linked Tacticus identity and SHALL return only that caller's hits in completion-time order. It SHALL NOT return or materialize another guild member's hits or participating units. This read SHALL remain server-side and SHALL NOT add raw hit history to the current-status response.

#### Scenario: Current user's hits are queried selectively

- **WHEN** a server-side consumer requests hits for the authenticated caller in one guild raid season
- **THEN** the read filters by that season and caller identity and returns only the requested projection for matching hits

#### Scenario: Other guild members have hits in the same season

- **WHEN** the current-user hit query runs for a season containing hits from multiple members
- **THEN** no other member's hit or unit rows are materialized or returned

### Requirement: Reading status never calls upstream; forced refresh is a separate cooldown-gated endpoint

`GET /api/v1/guilds/me/raid-status` SHALL always return the latest persisted observation for the caller's guild and SHALL NOT call the upstream Guild Raid API, regardless of how old that observation is.

`POST /api/v1/guilds/me/raid-status/refresh` SHALL perform the upstream call, persist a successful result, and return the same discriminated response. It SHALL be gated by a one-minute per-guild cooldown measured from the guild's last sync attempt, successful or failed; a request inside that window SHALL NOT call upstream and SHALL return the current persisted result instead of an error. Concurrent refresh requests for the same guild within one API instance SHALL share one upstream operation regardless of the cooldown.

#### Scenario: Read never triggers a sync

- **WHEN** any `GET` request arrives, fresh or long-stale
- **THEN** the response is served from the latest persisted observation and no upstream call is made

#### Scenario: Refresh within the cooldown reuses the last attempt

- **WHEN** a `POST /refresh` request arrives less than one minute after the guild's last sync attempt
- **THEN** the endpoint returns the current persisted result without calling upstream

#### Scenario: Manual and automatic refresh overlap

- **WHEN** multiple `POST /refresh` requests for the same guild require an upstream call concurrently
- **THEN** exactly one upstream request runs and every caller receives its result

### Requirement: Refresh failures distinguish transient unavailability from rejected source data

These failure responses SHALL only originate from `POST /refresh`; `GET` never produces them because it never calls upstream.

When `POST /refresh` fails because of transient unavailability or timeout and a retained successful observation exists, the endpoint SHALL return that observation with `freshness: stale` and its original `observedAt`, including after a process restart. Without retained data, transient unavailability or timeout SHALL produce a service-unavailable response.

When the upstream source rejects the guild credential or response data, `POST /refresh` SHALL produce a bad-gateway response even when retained status exists. A failed refresh or a successful no-active observation SHALL NOT delete previously retained active-season facts.

#### Scenario: Refresh fails with persisted retained data

- **WHEN** `POST /refresh` fails transiently and the guild has a previously persisted successful observation
- **THEN** that observation is returned with `freshness: stale`

#### Scenario: Refresh fails without usable retained data

- **WHEN** `POST /refresh` fails and no successful persisted observation exists
- **THEN** the endpoint returns the mapped upstream error and does not fabricate status

#### Scenario: Upstream credential is rejected while retained data exists

- **WHEN** `POST /refresh` is rejected because the guild credential is invalid and a successful observation was retained earlier
- **THEN** the endpoint returns bad gateway and does not serve the retained observation as stale
