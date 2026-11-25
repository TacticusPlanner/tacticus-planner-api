## MODIFIED Requirements

### Requirement: Reading status never calls upstream; forced refresh is a separate cooldown-gated endpoint

`GET /api/v1/guild/raid-status` SHALL always return the latest persisted observation for the caller's guild and SHALL NOT call the upstream Guild Raid API, regardless of how old that observation is.

`POST /api/v1/guild/raid-status/refresh` SHALL perform the upstream call, persist a successful result, and return the same discriminated response. It SHALL be gated by a one-minute per-guild cooldown measured from the guild's last sync attempt, successful or failed; a request inside that window SHALL NOT call upstream and SHALL return the current persisted result instead of an error, unless no successful observation has ever been persisted for the guild, in which case it SHALL return a service-unavailable response without calling upstream. Concurrent refresh requests for the same guild within one API instance SHALL share one upstream operation regardless of the cooldown. A successful refresh MAY start the background season capture defined by `guild-raid-season-capture`; its upstream requests are not part of that shared operation and SHALL NOT delay or change the refresh response.

#### Scenario: Read never triggers a sync

- **WHEN** any `GET` request arrives, fresh or long-stale
- **THEN** the response is served from the latest persisted observation and no upstream call is made

#### Scenario: Refresh within the cooldown reuses the last attempt

- **WHEN** a `POST /refresh` request arrives less than one minute after the guild's last sync attempt
- **THEN** the endpoint returns the current persisted result without calling upstream

#### Scenario: Refresh within the cooldown has no persisted result to reuse

- **WHEN** a `POST /refresh` request arrives less than one minute after the guild's last sync attempt and no successful observation has ever been persisted for the guild
- **THEN** the endpoint returns a service-unavailable response without calling upstream

#### Scenario: Manual and automatic refresh overlap

- **WHEN** multiple `POST /refresh` requests for the same guild require an upstream call concurrently
- **THEN** exactly one upstream current-status request runs and every caller receives its result

#### Scenario: Background capture fails

- **WHEN** a refresh succeeds and the season capture it starts fails
- **THEN** the refresh still returns its successful result
