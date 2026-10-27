# legendary-event-lane-points Specification

## Purpose
Defines what the `killPoints` of a served Legendary Event lane means and the consistency the catalog loader enforces between it and the lane's `defeatAll` points, so the base used for per-unit points cannot drift from the data the game shows.

## Requirements

### Requirement: A lane's killPoints is its defeat-all base

Every served `lres` lane SHALL carry `killPoints` equal to the first element of the lane's `defeatAll` array: the defeat-all score of the lane's first battle, used by clients as the lane's per-battle defeat-all base. The server SHALL NOT derive it from any other field.

#### Scenario: Uthar's lanes

- **WHEN** the `lres` dataset is served
- **THEN** `votanUthar` carries `killPoints` 30 on Alpha, 34 on Beta and 43 on Gamma, equal to the first `defeatAll` value of each lane

#### Scenario: Other events

- **WHEN** the dataset is served
- **THEN** `astarLysander` carries 32 / 38 / 31, `lostaBile` 37 / 40 / 46 and `tauFarsight` 34 / 26 / 17, each equal to its lane's first `defeatAll` value

### Requirement: The loader rejects a killPoints that disagrees with defeatAll

The catalog loader SHALL reject a raw `lres-*.json` lane whose `killPoints` differs from its first `defeatAll` value, or whose `defeatAll` is empty. The error SHALL name the event, the source file, the lane and both values.

#### Scenario: Copy error

- **GIVEN** a raw Uthar file whose Alpha `killPoints` is 34 while its first `defeatAll` value is 30
- **WHEN** the catalog is loaded
- **THEN** loading fails naming `votanUthar`, its file, the Alpha lane, `killPoints` 34 and `defeatAll[0]` 30

#### Scenario: Consistent file

- **GIVEN** a raw file whose three lanes each have `killPoints` equal to `defeatAll[0]`
- **WHEN** the catalog is loaded
- **THEN** the file loads

#### Scenario: Empty defeatAll

- **GIVEN** a raw lane with an empty `defeatAll`
- **WHEN** the catalog is loaded
- **THEN** loading fails naming the lane
