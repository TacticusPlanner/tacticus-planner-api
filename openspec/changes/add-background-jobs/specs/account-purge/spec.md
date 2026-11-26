## ADDED Requirements

### Requirement: A failed sign-in deletion is retried in the background

When `DELETE account?deleteSignInAccount=true` does not delete the Entra sign-in account during the request, the system SHALL retry the deletion in the background, provided the token carries a valid `oid` and a Graph credential is configured. Retries SHALL be bounded. A retry that finds the user already gone SHALL count as done. The response contract SHALL NOT change: the header still reports `false`.

#### Scenario: Graph fails during the request

- **GIVEN** a Graph credential is configured and Graph cannot delete the user during the request
- **WHEN** the caller sends `DELETE account?deleteSignInAccount=true` with a valid `oid`
- **THEN** the response is 204 with `X-Sign-In-Account-Deleted: false`, and a background deletion for that `oid` is queued

#### Scenario: Graph recovers

- **GIVEN** a queued background deletion
- **WHEN** Graph accepts the deletion on a later attempt
- **THEN** the sign-in account is deleted and no further attempts are made

#### Scenario: Deletion succeeds during the request

- **WHEN** Graph deletes the user during the request
- **THEN** the response reports `true` and nothing is queued
