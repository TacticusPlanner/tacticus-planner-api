## MODIFIED Requirements

### Requirement: Enum wire values are stable
Enum fields SHALL serialize to their established PascalCase wire strings (e.g. `Active`, `Rank`, `Character`); renaming or removing a wire value is a breaking contract change. Requests SHALL accept those values case-insensitively. A request with a value outside the set SHALL be rejected with a 400 `validation` problem whose `errors` names the field, and SHALL change nothing.

#### Scenario: Response values are PascalCase
- **WHEN** a client reads a goal
- **THEN** `status`, `goalType` and `entityType` carry PascalCase strings (e.g. `Active`, `Rank`, `Character`)

#### Scenario: Lower-case request value is accepted
- **WHEN** a client creates a goal with `"entityType": "character"`
- **THEN** the goal is created with entity type `Character`

#### Scenario: Unknown value is rejected by field
- **WHEN** a client creates a goal with `"goalType": "teleport"`
- **THEN** the API answers 400 with issue code `validation`
- **AND** `errors` has an entry whose key ends with `goalType`
- **AND** no goal is created
