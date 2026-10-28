## MODIFIED Requirements

### Requirement: The served array is derived from the raw run starts

The served `eventStageStartDatesUtc` SHALL be the raw file's `runStartDatesUtc` with each entry normalised to ISO 8601 UTC, in the same order. A raw file with an absent or empty `runStartDatesUtc` SHALL serve an empty array.

#### Scenario: Event with no announced run

- **GIVEN** a raw file whose `runStartDatesUtc` is empty or absent
- **WHEN** the dataset is served
- **THEN** the record carries `eventStageStartDatesUtc` `[]`
