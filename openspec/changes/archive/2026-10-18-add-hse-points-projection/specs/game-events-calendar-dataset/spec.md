## MODIFIED Requirements

### Requirement: Required parameters are enforced per definition
When an `event-definitions` record declares required parameters, the catalog build SHALL fail if any occurrence referencing that definition omits a value for one of them. An occurrence of a `HomeScreenEvent` definition MAY also carry the optional parameter `hseVariant`; when present it SHALL name a variant of that definition's `config.hse` (see `home-screen-event-scoring`), or the catalog build SHALL fail.

#### Scenario: Missing a declared required parameter fails the build
- **WHEN** an occurrence references a definition that declares a required parameter
- **AND** the occurrence's `parameters` omits a value for that parameter
- **THEN** the catalog build fails validation

#### Scenario: All required parameters present passes validation
- **WHEN** an occurrence supplies a value for every parameter its definition requires
- **THEN** the catalog build does not fail validation for that occurrence

#### Scenario: Known HSE variant passes validation
- **WHEN** an `hse-faction-focus` occurrence carries `hseVariant` `october-2026` and the definition has that variant
- **THEN** the catalog build does not fail validation and the occurrence is served with the parameter

#### Scenario: Unknown HSE variant fails the build
- **WHEN** an `hse-defeat-waves` occurrence carries `hseVariant` `run-3` and the definition has no such variant
- **THEN** the catalog build fails validation
