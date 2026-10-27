## MODIFIED Requirements

### Requirement: No shared reward ladder dataset is served

The catalog SHALL NOT serve an `lre-common` dataset: it SHALL be absent from the public manifest and its route SHALL respond 404.

#### Scenario: Manifest no longer lists lre-common

- **WHEN** the client reads the public manifest
- **THEN** it contains `lres` and `lre-battles` and no `lre-common` entry
