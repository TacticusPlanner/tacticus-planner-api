# userjot-identity-token Specification

## Purpose
Issues a short-lived, server-signed identity token so the UserJot widget can trust the identity of a signed-in Tacticus Planner user instead of accepting an unverified claim from the browser.

## Requirements

### Requirement: Signed identity token for the current authenticated user

The system SHALL provide an endpoint that, given a valid authenticated request, returns a JSON Web Token signed with HS256 using the workspace's configured UserJot project secret. The token's claims SHALL include `sub` (the caller's stable application user id), `iss` (the configured UserJot project id), `aud` (the literal string `userjot`), `iat` (the issue time), and `exp` (no more than one hour after `iat`).

#### Scenario: Authenticated user requests a token

- **WHEN** an authenticated user calls the token endpoint
- **THEN** the response contains a JWT whose claims are `sub` equal to that user's application id, `iss` equal to the configured UserJot project id, `aud` equal to `userjot`, `iat` equal to the current time, and `exp` no more than one hour after `iat`

#### Scenario: Each call issues a fresh token

- **WHEN** the same authenticated user calls the token endpoint twice
- **THEN** each response contains a distinct token with its own `iat`/`exp`, and neither call reuses or extends a previously issued token

### Requirement: Token carries the account's display name, and nothing else identifying

The token SHALL include a `firstName` claim equal to the current user's display name, using the same value the rest of the product shows for that account. The token SHALL NOT include the user's email address or any other contact information.

#### Scenario: Token reflects the account's display name

- **WHEN** an authenticated user requests a token
- **THEN** the token's `firstName` claim equals that account's current display name

#### Scenario: Token never carries an email address

- **WHEN** an authenticated user requests a token
- **THEN** the token contains no `email` claim, regardless of what email address is on file for that account

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
