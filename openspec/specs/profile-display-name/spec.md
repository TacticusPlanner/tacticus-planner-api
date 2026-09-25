# profile-display-name Specification

## Purpose

Separates a user's editable public planner name from authentication-provider identity and requires the user to choose it before public use.

## Requirements

### Requirement: The application owns one chosen profile display name

A profile's display name SHALL exist only once the user has submitted it. `GET /me` SHALL return `displayName` as that name or `null` while none is set, and `suggestedDisplayName` as a private editable suggestion (derived from the provider name claim while no name is set) or `null`. A provider claim SHALL NOT be stored as the name. This SHALL not affect issuer, subject, or email.

#### Scenario: First access with a provider name

- **WHEN** a new account is provisioned with a provider `name` claim
- **THEN** `/me` reports a private editable suggestion and no `displayName`

#### Scenario: No provider name

- **WHEN** a new account is provisioned with no `name` or `preferred_username` claim
- **THEN** `/me` reports no suggestion and no `displayName`

#### Scenario: Chosen name hides the suggestion

- **WHEN** a user has set a name
- **THEN** `/me` returns it as `displayName` and returns no `suggestedDisplayName`

### Requirement: An authenticated user can set or edit their name

`PUT /me/display-name` SHALL accept `{ "displayName": string }` for the caller's own profile, trim surrounding whitespace, require 1–80 characters after trimming, reject control characters, and persist the accepted value. It SHALL return the saved name. No uniqueness restriction is imposed. Failure SHALL leave the previous name and state unchanged.

#### Scenario: First save

- **WHEN** an authenticated user submits `"  Ada  "`
- **THEN** the saved and returned name is `"Ada"`, and a fresh `/me` read reports the same value

#### Scenario: Invalid name

- **WHEN** a user submits whitespace only or more than 80 characters after trimming
- **THEN** the endpoint returns a validation error without changing the profile

#### Scenario: Another user's profile cannot be edited

- **WHEN** an authenticated user submits a display-name update
- **THEN** only the profile bound to that user's authentication identity can change

### Requirement: Successful V1 authentication can propose a suggestion

After successful V1 authentication and profile retrieval, `POST /me/v1-import` SHALL return the submitted, trimmed V1 username as `suggestedDisplayName` when it is non-empty and the profile has no name set. It SHALL NOT store it or set it as the name, and SHALL NOT return it once a name is set, including on later re-import. Failed authentication or profile retrieval SHALL return no suggestion and change nothing. The V1 password SHALL not be persisted.

#### Scenario: V1 setup proposes the login username

- **WHEN** a new user with no name set successfully imports with V1 username `Ragnar42`
- **THEN** the import response carries `Ragnar42` as `suggestedDisplayName`, and `/me` still reports no `displayName`

#### Scenario: Later import preserves an edited name

- **GIVEN** a user has set the name `My Planner Name`
- **WHEN** the user later imports a V1 profile using username `legacy-name`
- **THEN** the response carries no suggestion and the name remains `My Planner Name`

#### Scenario: Failed V1 login proposes nothing

- **WHEN** V1 authentication fails
- **THEN** no suggestion is returned and the profile is unchanged
