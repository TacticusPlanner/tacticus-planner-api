## Purpose

Separates a user's editable public planner name from authentication-provider identity and requires explicit confirmation before public use.

## ADDED Requirements

### Requirement: The application owns one confirmed profile display name

Each profile SHALL have a confirmation state for its existing display-name value. `GET /me` SHALL return `displayName` as the confirmed name or `null`, `suggestedDisplayName` as a private editable suggestion or `null`, and `isDisplayNameConfirmed` as a boolean. A provider claim or legacy value SHALL NOT be treated as confirmed solely because it is non-empty. This state SHALL not affect issuer, subject, or email.

#### Scenario: First access with a provider name

- **WHEN** a new account is provisioned with a provider `name` claim
- **THEN** `/me` reports an unconfirmed state, a private editable suggestion, and no confirmed `displayName`

#### Scenario: Legacy account migrates conservatively

- **WHEN** an account created before the confirmation marker is read after migration
- **THEN** its existing name remains available as a private suggestion but is not considered confirmed

### Requirement: An authenticated user can confirm or edit their name

`PUT /me/display-name` SHALL accept `{ "displayName": string }` for the caller's own profile, trim surrounding whitespace, require 1–80 characters after trimming, reject control characters, persist the accepted value, and mark it confirmed. It SHALL return the saved confirmed name and confirmation state. No uniqueness restriction is imposed. Failure SHALL leave the previous name and state unchanged.

#### Scenario: First confirmation

- **WHEN** an authenticated user submits `"  Ada  "`
- **THEN** the saved and returned name is `"Ada"`, confirmation is true, and a fresh `/me` read reports the same value

#### Scenario: Invalid name

- **WHEN** a user submits whitespace only or more than 80 characters after trimming
- **THEN** the endpoint returns a validation error without changing the profile

#### Scenario: Another user's profile cannot be edited

- **WHEN** an authenticated user submits a display-name update
- **THEN** only the profile bound to that user's authentication identity can change

### Requirement: Successful V1 authentication can seed an unconfirmed suggestion

After successful V1 authentication and profile retrieval, the submitted V1 username SHALL replace the current unconfirmed suggestion if it is non-empty after trimming. It SHALL NOT confirm the name automatically and SHALL NOT overwrite a confirmed name, including on later re-import. Failed authentication or profile retrieval SHALL not change the suggestion. The V1 password SHALL not be persisted.

#### Scenario: V1 setup proposes the login username

- **WHEN** a new unconfirmed user successfully imports with V1 username `Ragnar42`
- **THEN** `/me` returns `Ragnar42` as the private suggestion and still requires confirmation

#### Scenario: Later import preserves an edited name

- **GIVEN** a user has confirmed `My Planner Name`
- **WHEN** the user later imports a V1 profile using username `legacy-name`
- **THEN** the confirmed name remains `My Planner Name`

#### Scenario: Failed V1 login preserves the suggestion

- **WHEN** V1 authentication fails
- **THEN** the current suggestion and confirmation state are unchanged
