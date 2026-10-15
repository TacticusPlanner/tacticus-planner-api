# legendary-event-clear-estimate-dataset Specification

## Purpose

Defines the served `legendary-event-clear-estimate` catalog dataset: the power ratio the client uses to estimate how far a team or unit clears on a Legendary Event lane, its calibration metadata, defaults and validation.

## ADDED Requirements

### Requirement: The catalog serves one clear-estimate record

The catalog SHALL serve a dataset `legendary-event-clear-estimate` containing exactly one record with these fields:

```
{
  id: "legendary-event-clear-estimate",
  powerRatio: number,                         // (0, 10]
  calibration: { sampleCount: int,            // >= 0
                 calibratedOn: string | null }  // "yyyy-MM-dd" when sampleCount > 0, else null
}
```

The served record SHALL equal the raw `legendary-event-clear-estimate.json`. The dataset SHALL be listed in the public manifest with its content hash. No display strings are served.

Assumptions:

- `powerRatio` compares a team's summed effective combat power (combat power × `horde-mode-tiers` multiplier) with a battle's catalog `power`, for every Legendary Event lane and battle.

#### Scenario: Initial dataset

- **GIVEN** the raw file has `powerRatio` 1.0 and `calibration` `{ sampleCount: 0, calibratedOn: null }`
- **WHEN** the client reads `legendary-event-clear-estimate`
- **THEN** it receives one record with those values

#### Scenario: Listed in the manifest

- **WHEN** the client reads the public manifest
- **THEN** it contains a `legendary-event-clear-estimate` entry with a content hash

### Requirement: The clear-estimate record is validated on load

The catalog loader SHALL reject the raw file, with a validation error naming the dataset and the offending value, when: it does not contain exactly one record; the id is not `legendary-event-clear-estimate`; `powerRatio` is not in (0, 10]; `calibration` is missing; `sampleCount` is negative; or `calibratedOn` is not a valid date while `sampleCount` > 0, or is not null while `sampleCount` is 0.

#### Scenario: Ratio out of bounds

- **GIVEN** `powerRatio` 0
- **WHEN** the catalog loads
- **THEN** loading fails naming `legendary-event-clear-estimate` and `powerRatio`

#### Scenario: Calibration date without samples

- **GIVEN** `sampleCount` 0 and `calibratedOn` "2026-10-12"
- **WHEN** the catalog loads
- **THEN** loading fails naming `calibratedOn`
