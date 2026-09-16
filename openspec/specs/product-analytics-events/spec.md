# product-analytics-events Specification

## Purpose

Defines the product events the API itself reports to the external analytics destination — moments that complete on the server and that the browser cannot observe or could misreport — along with the privacy floor those events must respect and the guarantee that analytics never affects the request that produced it.

## Requirements

### Requirement: A completed account registration is reported

The system SHALL report an `account_registered` event when an account is provisioned for a caller who did not previously have one. The event SHALL be reported at most once per account: a subsequent request by an already-provisioned caller SHALL NOT report it again.

#### Scenario: First access by a new user

- **WHEN** an authenticated caller with no existing account accesses the API for the first time and an account is provisioned for them
- **THEN** an `account_registered` event is reported, attributed to that account's analytics id

#### Scenario: Returning user does not re-report

- **WHEN** an authenticated caller who already has a provisioned account makes further requests
- **THEN** no further `account_registered` event is reported for that account

#### Scenario: Provisioning fails

- **WHEN** account provisioning does not complete successfully
- **THEN** no `account_registered` event is reported

### Requirement: A newly registered guild is reported

The system SHALL report a `guild_registered` event when a guild registration succeeds and results in a guild that was not previously registered for that profile. A successful re-registration that updates an already-registered guild SHALL NOT report the event, so the event measures adoption rather than repeated registration.

#### Scenario: A guild is registered for the first time

- **WHEN** a caller successfully registers a guild that was not previously registered for their profile
- **THEN** a `guild_registered` event is reported, attributed to that caller's analytics id

#### Scenario: Re-registering an existing guild

- **WHEN** a caller successfully re-registers a guild that is already registered for their profile
- **THEN** no `guild_registered` event is reported

#### Scenario: Registration is rejected

- **WHEN** a guild registration request fails validation, authorization, or the upstream call
- **THEN** no `guild_registered` event is reported

### Requirement: Reported events carry no personal or secret data

An event reported by the system SHALL identify the subject only by their analytics id. It SHALL NOT carry the account identifier, a display name, an email address, a Tacticus API key or user id, a guild name or other user-authored or free-text value, or any credential — whether as the event's identity or as one of its properties.

#### Scenario: Event properties exclude identifying values

- **WHEN** the system reports any product event
- **THEN** the event's identity is the analytics id, and none of its properties contain an account identifier, display name, email address, Tacticus API key, Tacticus user id, guild name, or other user-authored text

### Requirement: Analytics never affects the operation that produced it

Reporting an event SHALL NOT change the outcome, response, or observable timing behavior of the request that triggered it. If the analytics destination is slow, unreachable, or returns an error, the triggering request SHALL still succeed and return its normal response.

#### Scenario: The analytics destination is unreachable

- **WHEN** an operation that reports an event completes successfully while the analytics destination cannot be reached
- **THEN** the operation returns its normal successful response, and the failure is not surfaced to the caller

#### Scenario: Reporting does not block the response

- **WHEN** an operation reports an event
- **THEN** the operation's response is not delayed waiting for the analytics destination to acknowledge it

### Requirement: Analytics is inert when not configured

When no analytics destination is configured, the system SHALL run normally and SHALL NOT attempt to contact any analytics destination. Every behavior other than event reporting SHALL be unchanged, so that local development and automated tests neither require nor produce outbound analytics traffic.

#### Scenario: Running without analytics configuration

- **WHEN** the system starts with no analytics destination configured and an operation that would report an event is performed
- **THEN** the operation completes normally and no outbound analytics request is attempted

#### Scenario: Automated tests emit nothing

- **WHEN** the automated test suite exercises operations that report events
- **THEN** no outbound analytics request leaves the test process
