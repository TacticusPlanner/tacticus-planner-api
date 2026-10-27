## MODIFIED Requirements

### Requirement: Saving can leave an alliance unset

The PUT request SHALL accept `imperial`, `xenos` and `chaos` each as either `{ sector, tier }` (validated: sector one of Stone, Iron, Bronze, Silver, Gold, Diamond, Adamantine; tier 1–4) or `null`. A `null` alliance SHALL be stored as "not set" (no override record), replacing any previously saved position for it.

#### Scenario: One alliance left unset

- **WHEN** the profile PUTs Imperial Gold 2, Xenos `null`, Chaos Iron 4
- **THEN** the response is Imperial `{ Gold, 2, isSet: true }`, Xenos `{ Stone, 1, isSet: false }`, Chaos `{ Iron, 4, isSet: true }`

#### Scenario: Invalid non-null alliance still rejected

- **WHEN** the profile PUTs Imperial `{ "Wood", 1 }`
- **THEN** the API answers 400 and nothing is stored
