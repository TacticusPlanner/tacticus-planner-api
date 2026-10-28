## MODIFIED Requirements

### Requirement: Lane points come from the Acing objective

Each battle's first objective SHALL be `Acing`; its score is that battle's `defeatAll` value (the track's `killPoints` follows `legendary-event-lane-points`). A battle without a leading `Acing` objective SHALL fail the load.

#### Scenario: Defeat-all points

- **GIVEN** a track whose 18 battles have `Acing` scores of 37, except 45 in battle 18
- **WHEN** the catalog loads
- **THEN** the track has `killPoints` 37 and `defeatAll` seventeen 37s followed by 45
