# onslaught-progress-overrides Specification

## Purpose
Defines how the onslaught-progress player-data override reports whether each alliance (Imperial, Xenos, Chaos) has saved progress, and how saving and placeholder values behave for an alliance that was never set.

## Requirements

### Requirement: Onslaught progress reports whether each alliance was saved

The onslaught-progress response (`GET` and `PUT me/player-data-overrides/onslaught-progress`) SHALL carry, for each of `imperial`, `xenos` and `chaos`, the fields `sector` (string), `tier` (int, 1–4, where 4 means the sector is complete) and `isSet` (boolean), plus the profile's `revision` (int64). `isSet` SHALL be derived from the stored overrides: `true` when an override record exists for that alliance, `false` otherwise. When `isSet` is `false`, `sector` SHALL be `"Stone"` and `tier` SHALL be `1` as a placeholder that clients MUST NOT treat as the player's progress.

#### Scenario: Nothing saved

- **WHEN** a profile that never saved Onslaught progress calls GET
- **THEN** each alliance is `{ "sector": "Stone", "tier": 1, "isSet": false }`

#### Scenario: Saved through PUT

- **WHEN** the profile PUTs Imperial Gold 2, Xenos Diamond 4, Chaos Silver 1 and then calls GET
- **THEN** the response is Imperial `{ Gold, 2, isSet: true }`, Xenos `{ Diamond, 4, isSet: true }`, Chaos `{ Silver, 1, isSet: true }`

#### Scenario: Saved Stone 1 is distinguishable from unset

- **WHEN** the profile PUTs Stone 1 for every alliance
- **THEN** each alliance is `{ "sector": "Stone", "tier": 1, "isSet": true }`

#### Scenario: Imported from V1

- **WHEN** the V1 import writes Onslaught progress
- **THEN** each imported alliance reports `isSet: true`

### Requirement: Saving can leave an alliance unset

The PUT request SHALL accept `imperial`, `xenos` and `chaos` each as either `{ sector, tier }` (validated: sector one of Stone, Iron, Bronze, Silver, Gold, Diamond, Adamantine; tier 1–4) or `null`. A `null` alliance SHALL be stored as "not set" (no override record), replacing any previously saved position for it. Revision and conflict handling SHALL be unchanged.

#### Scenario: One alliance left unset

- **WHEN** the profile PUTs Imperial Gold 2, Xenos `null`, Chaos Iron 4
- **THEN** the response is Imperial `{ Gold, 2, isSet: true }`, Xenos `{ Stone, 1, isSet: false }`, Chaos `{ Iron, 4, isSet: true }`

#### Scenario: Invalid non-null alliance still rejected

- **WHEN** the profile PUTs Imperial `{ "Wood", 1 }`
- **THEN** the API answers 400 and nothing is stored
