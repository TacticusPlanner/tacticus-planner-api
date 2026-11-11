# Spec Delta

## Purpose

Serves the latest and the previous game catalog release at the same time, so players whose game has not updated yet can keep planning against the release they play, and defines which server paths follow a player's selection.

## ADDED Requirements

### Requirement: The API serves a window of two catalog releases

The API SHALL serve exactly two catalog releases at once: `Latest` and `Previous`, each built from its own pinned datamine release, with the curated data shared. Each release SHALL be an immutable snapshot with its own `version`, `gameVersion`, `schemaHash`, `sourceHash` and dataset hashes. Both SHALL load and validate at startup; the API SHALL NOT start when either fails.

#### Scenario: Both releases load

- **GIVEN** `latest` is pinned to `v1.44.87.3088` and `previous` to `v1.43.157.2989`
- **WHEN** the API starts
- **THEN** it serves two releases whose `gameVersion` values are `1.44.87.3088` and `1.43.157.2989`

#### Scenario: A previous release that no longer validates stops startup

- **WHEN** the `previous` data fails catalog validation under the current code
- **THEN** startup fails and names the `previous` release, as a failing `latest` would

#### Scenario: Identical datasets keep their hash

- **GIVEN** a dataset, for example `factions`, whose served data is identical in both releases
- **WHEN** both manifests are read
- **THEN** that dataset's `hash` is the same in both manifests

### Requirement: Clients list the served releases

`GET /api/v1/game-catalog/versions` SHALL be anonymous and SHALL return the window, latest first:

```
GameCatalogVersions {
  versions: [{
    gameVersion: string,      // e.g. "1.44.87.3088"
    version: string,          // the release's manifest version, e.g. "1.44.87.3088+6a8049a9557b"
    role: "Latest" | "Previous"
  }]
}
```

It SHALL carry an ETag of its content and honour `If-None-Match`.

#### Scenario: Listing the window

- **WHEN** a client requests the versions while `1.44.87.3088` is latest and `1.43.157.2989` previous
- **THEN** the response lists `1.44.87.3088` with role `Latest` first and `1.43.157.2989` with role `Previous` second

#### Scenario: Unchanged window revalidates

- **WHEN** a client repeats the request with the ETag it received
- **THEN** the API answers `304 Not Modified`

### Requirement: The manifest is selected by game version

`GET /api/v1/game-catalog/manifest` SHALL accept an optional `gameVersion` query parameter. Without it, the API SHALL return the `Latest` manifest, unchanged from today. With a served game version it SHALL return that release's manifest. With any other value it SHALL answer `404 Not Found` with issue code `notFound` (see `api-error-contract`).

#### Scenario: Default stays latest

- **WHEN** a client requests the manifest without `gameVersion`
- **THEN** it receives the `Latest` release's manifest

#### Scenario: Requesting the previous release

- **WHEN** a client requests the manifest with `gameVersion=1.43.157.2989` while that release is `Previous`
- **THEN** it receives a manifest whose `gameVersion` is `1.43.157.2989` and whose dataset URLs resolve to that release's envelopes

#### Scenario: A release that left the window

- **WHEN** a client requests the manifest with `gameVersion=1.42.110.2900` after that release has left the window
- **THEN** the API answers `404` with issue code `notFound`

### Requirement: The window rolls one release at a time

A new datamine release SHALL enter as `Latest`; the former `Latest` SHALL become `Previous`; the former `Previous` SHALL leave the window. Stored user data SHALL be unaffected: catalog ids are stable, and a stored catalog version that has left the window SHALL resolve as described for user settings.

#### Scenario: Releasing 1.45

- **GIVEN** the window is `1.44.87.3088` (Latest) and `1.43.157.2989` (Previous)
- **WHEN** a deploy rolls in `1.45.x`
- **THEN** the window is `1.45.x` (Latest) and `1.44.87.3088` (Previous), and `1.43.157.2989` is no longer served

### Requirement: Catalog ids are accepted from either release

Every server path that validates a client-submitted catalog id SHALL accept an id that exists in either served release, and SHALL reject it only when neither has it. This covers:

- goal entities, upgrades, shops and battles;
- Legendary Event ids for plans and run records;
- project event targets;
- campaign, campaign-event, incursion and onslaught battle-progress overrides.

#### Scenario: A goal for a unit only in latest

- **WHEN** a player whose selected version is `1.43.157.2989` creates a goal for `lostaFearmonger`, which only `1.44.87.3088` has
- **THEN** the goal is accepted

#### Scenario: A battle only in previous

- **WHEN** a player records progress for a quest-event battle of `astarCyrus`, which only `1.43.157.2989` has
- **THEN** the override is accepted while `1.43.157.2989` is served

#### Scenario: An id in neither release

- **WHEN** a client submits a goal for an id neither release has
- **THEN** it is rejected exactly as an unknown id is today

### Requirement: Recorded catalog versions use the player's effective version

Rows that record the catalog release they were computed from SHALL record the `version` of the player's effective catalog release (see `user-settings`), not always `Latest`. This covers Legendary Event plans, Legendary Event run records and roster snapshots. V1 imports keep recording `v1-import`.

#### Scenario: A plan saved on the previous release

- **WHEN** a player whose effective version is `1.43.157.2989` saves a Legendary Event plan
- **THEN** the plan's `catalogVersion` is the `1.43.157.2989` release's `version`

### Requirement: Live-game paths always use the latest release

Paths that reflect the live game rather than a player's plan SHALL use the `Latest` release regardless of the player's selection:

- mapping Tacticus player data during sync (unit kinds, campaigns);
- guild raid status.

#### Scenario: Syncing a roster on the previous release

- **WHEN** a player whose selected version is `1.43.157.2989` syncs and their roster contains `lostaFearmonger`
- **THEN** the sync classifies `lostaFearmonger` with the `Latest` release and stores it like any other unit
