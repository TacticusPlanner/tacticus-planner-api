## ADDED Requirements

### Requirement: An edited upgrade target is bounded

A target edit of an Upgrade goal SHALL carry at most 250 upgrade targets, the same bound as creation. More SHALL be a 400 validation problem that changes nothing.

#### Scenario: Too many upgrade targets

- **WHEN** an Upgrade goal's target is edited with 251 targets
- **THEN** the response is 400 `validation` and the goal is unchanged
