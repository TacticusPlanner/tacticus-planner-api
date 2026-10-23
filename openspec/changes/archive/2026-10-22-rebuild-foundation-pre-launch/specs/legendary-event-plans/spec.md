## MODIFIED Requirements

### Requirement: One revision per plan guards every mutation

Every mutation (`PUT` plan, `POST`/`PUT`/`DELETE` team, `PUT` order, `PUT` run inputs, `PUT` annotations) SHALL carry `expectedRevision`. When it differs from the plan's current revision (0 for a plan that does not exist yet), the system SHALL make no change and respond 409 in the unified error shape (`api-error-contract`) with `issueCode` `legendaryEventPlanStale` and the current plan under `current`. A concurrent write detected at save time, including a unique-key violation when two callers create the missing plan at once, SHALL produce the same response after re-reading the plan. Every successful mutation SHALL bump the plan revision by exactly one and respond with the whole plan, with three exceptions that succeed without bumping the revision: a `PUT …/teams/order` whose order is already current (see "Lane order is replaced as a whole set"), and a `PUT …/runs/{run}` whose values equal the stored run inputs, or are all zero/false for a run with no stored inputs (see "Run inputs are stored per run"), and a `PUT …/annotations` that changes no cell (see "Maybe clear and Stop here annotations are stored per objective cell").

#### Scenario: Stale revision

- **GIVEN** a plan at revision 3
- **WHEN** the caller creates a team with `expectedRevision` 2
- **THEN** the response is 409 with `issueCode` `legendaryEventPlanStale` and `current` holding the plan at revision 3, and no team is created

#### Scenario: Stale revision on a run write

- **GIVEN** a plan at revision 3
- **WHEN** the caller writes run 1 inputs with `expectedRevision` 2
- **THEN** the response is 409 with `issueCode` `legendaryEventPlanStale` and `current` holding the plan at revision 3, and no run input changes

#### Scenario: Lazy creation requires revision 0

- **GIVEN** no plan exists for the event
- **WHEN** a team is created with `expectedRevision` 1
- **THEN** the response is 409 with `current` holding the empty revision-0 plan

#### Scenario: Concurrent first writers

- **GIVEN** no plan exists for the event
- **WHEN** two callers each create a team with `expectedRevision` 0 at the same time
- **THEN** exactly one plan exists, one caller succeeds with revision 1 and the other receives 409 carrying that plan

#### Scenario: Concurrent writers

- **GIVEN** two callers hold the plan at revision 3
- **WHEN** both send a team create with `expectedRevision` 3
- **THEN** exactly one succeeds with revision 4 and the other receives 409 carrying the plan at revision 4
