## MODIFIED Requirements

### Requirement: Imported goals are created by the import operation

The import operation SHALL create the imported goals itself. It SHALL NOT
return goal-creation requests for the caller to submit. Goals SHALL be created
in the caller's default project, each in the status its own V1 daily-planning
choice implies (see `goal-lifecycle-status`) and never one derived from which
project is the caller's active plan.

The import SHALL NOT require the caller to supply, or re-supply, anything
derived from the V1 profile: V1 credentials are used once, within the same
operation, and are not retained.

#### Scenario: Import creates goals in one operation

- **GIVEN** an account with player data and a V1 profile containing supported goals
- **WHEN** the goals part is imported
- **THEN** the goals exist on the account when the operation returns, and the response contains
  no goal-creation request for the caller to submit

#### Scenario: Import is repeatable without duplicating goals

- **GIVEN** an import has already created goals for an account
- **WHEN** the same V1 profile is imported again
- **THEN** no duplicate goals are created, and each already-present goal reports as skipped
  because it already exists
