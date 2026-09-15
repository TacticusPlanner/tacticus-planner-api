## Purpose

Issues a short-lived, server-signed identity token so the UserJot widget can trust the identity of a signed-in Tacticus Planner user instead of accepting an unverified claim from the browser.

## ADDED Requirements

### Requirement: Signed identity token for the current authenticated user

The system SHALL provide an endpoint that, given a valid authenticated request, returns a JSON Web Token signed with HS256 using the workspace's configured UserJot project secret. The token's claims SHALL include `sub` (the caller's stable application user id), `iss` (the configured UserJot project id), `aud` (the literal string `userjot`), `iat` (the issue time), and `exp` (no more than one hour after `iat`).

#### Scenario: Authenticated user requests a token

- **WHEN** an authenticated user calls the token endpoint
- **THEN** the response contains a JWT whose claims are `sub` equal to that user's application id, `iss` equal to the configured UserJot project id, `aud` equal to `userjot`, `iat` equal to the current time, and `exp` no more than one hour after `iat`

#### Scenario: Each call issues a fresh token

- **WHEN** the same authenticated user calls the token endpoint twice
- **THEN** each response contains a distinct token with its own `iat`/`exp`, and neither call reuses or extends a previously issued token

### Requirement: Token carries known profile fields

The token SHALL include `email`, `firstName`, and `lastName` claims when that information is known for the current user, using the same values the rest of the product shows for that account. A field with no known value SHALL be omitted from the token rather than sent as an empty or placeholder value.

#### Scenario: User has a known display name and email

- **WHEN** an authenticated user with a display name and email requests a token
- **THEN** the token includes `email`, `firstName`, and `lastName` claims matching that account's current profile data

#### Scenario: User has no display name on file

- **WHEN** an authenticated user with no display name on file requests a token
- **THEN** the token omits the `firstName`/`lastName` claims rather than including empty strings

### Requirement: Unauthenticated requests are rejected

The endpoint SHALL reject a request that is not authenticated and SHALL NOT issue a token for it.

#### Scenario: Unauthenticated request

- **WHEN** a request without valid authentication calls the token endpoint
- **THEN** the system responds with an authentication error and issues no token

### Requirement: The signing secret is never exposed to clients

No response returned by the system SHALL contain the UserJot project secret used to sign tokens, in whole or in part.

#### Scenario: Token response contains only the signed token

- **WHEN** the token endpoint returns a successful response
- **THEN** the response body contains the signed token and no field derived from the raw project secret
