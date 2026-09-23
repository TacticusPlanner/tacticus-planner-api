## MODIFIED Requirements

### Requirement: Token carries the account's display name, and nothing else identifying

For a confirmed profile, the token SHALL include a `firstName` claim equal to the current confirmed display name, using the same value the rest of the product shows for that account. For an unconfirmed profile, `firstName` SHALL be a fixed, non-identifying product fallback, never a provider claim, V1 username, email-like suggestion, or legacy stored value. The token SHALL NOT include the user's email address or any other contact information.

#### Scenario: Token reflects the account's display name

- **WHEN** an authenticated user with a confirmed display name requests a token
- **THEN** the token's `firstName` claim equals that account's current confirmed display name

#### Scenario: Token never carries an email address

- **WHEN** an authenticated user requests a token
- **THEN** the token contains no `email` claim, regardless of what email address is on file for that account

#### Scenario: Unconfirmed account is anonymous by name

- **GIVEN** an unconfirmed profile whose stored suggestion resembles an email address
- **WHEN** that user requests a token
- **THEN** `firstName` is the fixed generic fallback and neither that suggestion nor a provider value occurs in the token
