## Purpose

Defines the supported unit-centered goal target model after removal of independent equipment goals.

## Requirements

### Requirement: Goals target Characters or Machines of War

The goal API SHALL accept Character and Mow entity types and SHALL reject unsupported entity discriminators. It SHALL not expose Item as a goal entity type.

#### Scenario: Item goal request is rejected

- **WHEN** a caller submits entity type Item
- **THEN** validation rejects the request as unsupported

### Requirement: UpgradeItem goal type is removed

The goal API and generated OpenAPI SHALL not expose UpgradeItem or its equipment-level config. Ordinary Character/MoW Upgrade goals SHALL remain supported.

#### Scenario: UpgradeItem request is rejected

- **WHEN** a caller submits goal type UpgradeItem
- **THEN** validation rejects the request as unsupported

#### Scenario: Unit Upgrade remains supported

- **WHEN** a valid Character/MoW Upgrade request contains valid upgrade-material targets
- **THEN** creation succeeds

### Requirement: Persisted independent equipment goals are removed

The migration SHALL delete existing goals whose entity type is Item or goal type is UpgradeItem and SHALL cascade their project memberships without deleting other goals.

#### Scenario: Migration preserves ordinary goals

- **GIVEN** the database contains equipment and Character/MoW goals
- **WHEN** the migration applies
- **THEN** only independent equipment goals and their memberships are deleted

### Requirement: Material target terminology is unambiguous

Domain/API symbols for ordinary Upgrade goal targets SHALL use UpgradeMaterial terminology while preserving the wire element fields `upgradeId` and `quantity`.

#### Scenario: OpenAPI describes material targets

- **WHEN** OpenAPI is generated
- **THEN** ordinary Upgrade request/response schemas use material-target names and contain `upgradeId` and `quantity`
