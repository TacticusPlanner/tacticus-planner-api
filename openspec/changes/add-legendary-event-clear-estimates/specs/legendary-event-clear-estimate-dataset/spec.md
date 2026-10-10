# legendary-event-clear-estimate-dataset Specification

## Purpose

Defines the served `lre-clear-estimate` catalog dataset: the power ratio and per-unit efficiency coefficients the client uses to estimate how far a team or unit clears on a Legendary Event lane, its calibration metadata, defaults and validation.

## ADDED Requirements

### Requirement: The catalog serves one clear-estimate record

The catalog SHALL serve a dataset `lre-clear-estimate` containing exactly one record with these fields:

```
{
  id: "lre-clear-estimate",
  powerRatio: number,                         // (0, 10]
  calibration: { sampleCount: int,            // >= 0
                 calibratedOn: string | null }, // "yyyy-MM-dd" when sampleCount > 0, else null
  unitCoefficients: { unitId: string, coefficient: number }[]   // (0, 5], ascending by unitId
}
```

The served record SHALL equal the raw `lre-clear-estimate.json` except that `unitCoefficients` is sorted by `unitId` (ordinal). The server SHALL NOT expand defaults: a unit absent from `unitCoefficients` means coefficient 1.0, applied by the client. The dataset SHALL be listed in the public manifest with its content hash. No display strings are served.

Assumptions:

- One coefficient per unit applies to every Legendary Event lane and battle.
- `powerRatio` compares a team's summed effective combat power with a battle's catalog `power`.

#### Scenario: Initial dataset

- **GIVEN** the raw file has `powerRatio` 1.0, `calibration` `{ sampleCount: 0, calibratedOn: null }` and no coefficients
- **WHEN** the client reads `lre-clear-estimate`
- **THEN** it receives one record with those values and an empty `unitCoefficients`

#### Scenario: Coefficients are sorted

- **GIVEN** the raw file lists `ultraTigurius` 1.2 before `bloodDante` 0.9
- **WHEN** the catalog is built
- **THEN** the served `unitCoefficients` lists `bloodDante` then `ultraTigurius`, and the hash is the same as for the reverse authoring order

#### Scenario: Listed in the manifest

- **WHEN** the client reads the public manifest
- **THEN** it contains an `lre-clear-estimate` entry with a content hash

### Requirement: The clear-estimate record is validated on load

The catalog loader SHALL reject the raw file, with a validation error naming the dataset and the offending value, when: it does not contain exactly one record; `powerRatio` is not in (0, 10]; `sampleCount` is negative; `calibratedOn` is not a valid date while `sampleCount` > 0, or is not null while `sampleCount` is 0; a `unitId` is not a catalog character id; a `coefficient` is not in (0, 5]; or a `unitId` appears twice.

#### Scenario: Unknown unit

- **GIVEN** the raw file lists `notAUnit` with coefficient 1.1
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `lre-clear-estimate` and `notAUnit`

#### Scenario: Coefficient out of bounds

- **GIVEN** the raw file lists `ultraCalgar` with coefficient 0
- **WHEN** the catalog loads
- **THEN** loading fails naming `ultraCalgar` and the coefficient

#### Scenario: Calibration date without samples

- **GIVEN** `sampleCount` 0 and `calibratedOn` "2026-10-12"
- **WHEN** the catalog loads
- **THEN** loading fails naming `calibratedOn`
