# incursion-progress-overrides Specification

## Purpose
Defines the Incursion-progress player-data override: the stars the player has earned on each Incursion battle, per Machine of War and tier. Tiers can be played in any order and a run may take either branch of a tier's run map, so progress is kept per battle rather than as a count of tiers. The public player API does not expose Incursion progress, so the planner stores it as a manual override next to the other player-data overrides.

## Requirements

### Requirement: Incursion progress is the stars per battle

The response of `GET` and `PUT player-data-overrides/incursions-progress` SHALL carry `battles`, a list of `{ mowId, tierIndex, battleId, stars }`, plus the profile's `revision` (int64). `tierIndex` is the catalog tier index and `battleId` the catalog battle id within that tier (battle ids repeat across tiers). A battle with no entry has no stars; stored entries always have `stars` between 1 and 3. A 3-star battle can be raided on later runs.

#### Scenario: Nothing saved

- **WHEN** a profile that never saved Incursion progress calls GET
- **THEN** `battles` is empty

#### Scenario: Saved through PUT

- **WHEN** the profile PUTs `tauBroadside` tier 12 battle `1` at 3 stars, `tauBroadside` tier 0 battle `1` at 1 star, `necroReanimator` tier 0 battle `2` at 2 stars and `blackForgefiend` tier 0 battle `1` at 0 stars, then calls GET
- **THEN** `battles` holds the first three entries (the zero entry is not stored)

### Requirement: Saving replaces all Incursion progress

The PUT request SHALL carry the whole `battles` list and the `revision` the client last read. That revision is shared by all of the profile's player-data overrides, so a mismatch SHALL be rejected with 409 `concurrency` carrying the current Incursion progress, and SHALL change nothing. Saving SHALL leave the profile's other player-data overrides unchanged. Each entry SHALL name a battle of a tier of a served Incursion (`incursions` dataset) at most once, with `stars` between 0 and 3; anything else (including a null entry) SHALL be rejected with 400 and SHALL change nothing.

#### Scenario: Unknown battle or bad stars

- **WHEN** the profile PUTs `notAMow`, a tier the Incursion does not have, a battle the tier does not have, 4 stars, -1 stars, or the same battle twice
- **THEN** the API answers 400 and nothing is stored

#### Scenario: Stale revision is rejected

- **WHEN** the profile PUTs with a `revision` older than the stored one
- **THEN** the API answers 409 `concurrency` with the current progress, and nothing is saved
