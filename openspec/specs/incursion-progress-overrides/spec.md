# incursion-progress-overrides Specification

## Purpose
Defines the Incursion-progress player-data override: how many Incursion tiers the player has cleared for each Machine of War. The public player API does not expose Incursion progress, so the planner stores it as a manual override next to the other player-data overrides.

## Requirements

### Requirement: Incursion progress is the tiers cleared per MoW

The response of `GET` and `PUT player-data-overrides/incursions-progress` SHALL carry `progress`, a list of `{ mowId, tiersCleared }`, plus the profile's `revision` (int64). `tiersCleared` counts cleared tiers from the first, and tiers unlock in order, so it is also the index of the next tier to play. A MoW with no entry has cleared none; stored entries always have `tiersCleared` ≥ 1.

#### Scenario: Nothing saved

- **WHEN** a profile that never saved Incursion progress calls GET
- **THEN** `progress` is empty

#### Scenario: Saved through PUT

- **WHEN** the profile PUTs `tauBroadside` 14, `necroReanimator` 3 and `blackForgefiend` 0, then calls GET
- **THEN** `progress` is `tauBroadside` 14 and `necroReanimator` 3 (the zero entry is not stored)

### Requirement: Saving replaces all Incursion progress

The PUT request SHALL carry the whole `progress` list and the `revision` the client last read. That revision is shared by all of the profile's player-data overrides, so a mismatch SHALL be rejected with 409 `concurrency` carrying the current Incursion progress, and SHALL change nothing. Saving SHALL leave the profile's other player-data overrides unchanged. Each entry SHALL name a served Incursion (`incursions` dataset) at most once, with `tiersCleared` between 0 and that Incursion's tier count; anything else SHALL be rejected with 400 and SHALL change nothing.

#### Scenario: Unknown MoW or too many tiers

- **WHEN** the profile PUTs `notAMow` 1, or `tauBroadside` 15 when its Incursion has 14 tiers
- **THEN** the API answers 400 and nothing is stored

#### Scenario: Stale revision is rejected

- **WHEN** the profile PUTs with a `revision` older than the stored one
- **THEN** the API answers 409 `concurrency` with the current progress, and nothing is saved
