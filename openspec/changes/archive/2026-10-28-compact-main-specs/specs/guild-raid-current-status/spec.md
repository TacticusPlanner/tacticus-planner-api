## MODIFIED Requirements

### Requirement: Current Guild Raid status is scoped to a ready registered guild

The system SHALL expose an authenticated `GET /api/v1/guild/raid-status` endpoint. It SHALL use the caller's linked guild and that guild's stored encrypted API token without returning the token or another member's hit history.

The endpoint SHALL return conflict status when the caller has no linked registered guild, the guild has never completed synchronization, the guild has no usable stored token, or the guild has never had a successful Guild Raid status observation persisted. An unprovisioned profile SHALL receive a not-found response.

#### Scenario: Ready guild requests its status

- **WHEN** an authenticated caller belongs to a registered guild with a successful synchronization and usable stored token
- **THEN** the endpoint returns the normalized current Guild Raid status for that guild

#### Scenario: Guild access is not ready

- **WHEN** the caller is not linked to a registered and successfully synchronized guild
- **THEN** the endpoint returns a conflict response that identifies the missing prerequisite without exposing any guild credential

#### Scenario: Guild is ready but has no raid-status observation yet

- **WHEN** the caller's guild is registered, synchronized, and holds a usable token, but no Guild Raid status observation has ever been persisted for it
- **THEN** the endpoint returns a conflict response instead of performing an inline first upstream fetch

### Requirement: Refresh failures distinguish transient unavailability from rejected source data

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

### Requirement: A retained observation that cannot be projected is a bad-gateway response

`GET /api/v1/guild/raid-status` projects the retained observation against the current game-catalog version at read time. If that retained observation can no longer be projected (for example, its season config or a referenced encounter is missing from the current catalog), the endpoint SHALL return a bad-gateway response rather than an empty or malformed body.

#### Scenario: Retained observation no longer resolves against the catalog

- **WHEN** `GET /raid-status` reads a retained observation whose season config or encounter data is no longer resolvable in the current game-catalog version
- **THEN** the endpoint returns bad gateway instead of an unhandled or empty response
