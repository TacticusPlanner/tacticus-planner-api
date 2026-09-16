# analytics-identity Specification

## Purpose

Gives each account a stable, pseudonymous identifier for use with external analytics destinations, so product usage can be attributed to a consistent person without disclosing the account's real identifier, its creation time, or a key that links the account across separate third-party systems.

## Requirements

### Requirement: Every account has a stable pseudonymous analytics id

The system SHALL derive, for each account, an analytics id that is stable for the lifetime of that account and identical every time it is derived. Two different accounts SHALL NOT share an analytics id. The analytics id SHALL be derived from the account using a server-held secret, and SHALL NOT be the account's own identifier in any encoding.

#### Scenario: The same account always derives the same id

- **WHEN** the analytics id is derived for the same account on two separate requests
- **THEN** both derivations produce the identical value

#### Scenario: Different accounts derive different ids

- **WHEN** the analytics id is derived for two different accounts
- **THEN** the two values differ

#### Scenario: The id is not the account identifier

- **WHEN** the analytics id is derived for an account
- **THEN** the value is not equal to that account's own identifier, and does not contain it in any encoding

### Requirement: The analytics id discloses nothing about the account it represents

An observer holding an analytics id and no other information SHALL NOT be able to determine the account's identifier, its creation time, or its position in account creation order. The analytics id SHALL NOT be computable by any party that does not hold the server-held secret, including a party that already knows the account's identifier.

#### Scenario: Creation time is not recoverable

- **WHEN** analytics ids are derived for two accounts created at different times
- **THEN** neither value reveals its account's creation time, and the ordering of the two values does not follow the order in which the accounts were created

#### Scenario: Knowing the account identifier is not enough to derive the id

- **WHEN** a party knows an account's identifier but not the server-held secret
- **THEN** that party cannot produce that account's analytics id

### Requirement: Each external destination receives a different analytics id

When an analytics id is derived for a named external destination, the value SHALL be specific to that destination. The same account SHALL derive a different value for each distinct destination, so that two external systems holding ids for the same person cannot recognise them as the same person by comparing those ids.

#### Scenario: Two destinations receive unlinkable ids for one account

- **WHEN** an analytics id is derived for the same account for two different named destinations
- **THEN** the two values differ, and neither can be transformed into the other without the server-held secret

### Requirement: The authenticated client receives its own analytics id

The current-user response SHALL include the analytics id belonging to the authenticated caller, so the client reports the same identity to the analytics destination that the server does. The response SHALL NOT include the analytics id of any other account.

#### Scenario: Current-user response carries the caller's analytics id

- **WHEN** an authenticated, provisioned user requests their current-user record
- **THEN** the response includes an analytics id, and it equals the value the server derives for that same caller

#### Scenario: Server and client agree on identity

- **WHEN** the server captures an event for an account and the client captures an event for the same signed-in account
- **THEN** both events are attributed to the same analytics id

### Requirement: The secret behind the analytics id is never exposed

No response returned by the system SHALL contain the secret used to derive analytics ids, in whole or in part, or any value from which it can be recovered.

#### Scenario: Responses never leak the derivation secret

- **WHEN** any endpoint returns a successful response
- **THEN** the response contains no field holding the derivation secret or any portion of it
