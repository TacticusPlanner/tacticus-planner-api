## MODIFIED Requirements

### Requirement: Token carries the account's display name, and nothing else identifying

For a profile with a display name, the token SHALL include a `firstName` claim equal to that name, using the same value the rest of the product shows for that account. For a profile with no name set, `firstName` SHALL be a fixed, non-identifying product fallback, never a provider claim, V1 username, or email-like suggestion. The token SHALL NOT include the user's email address or any other contact information.

#### Scenario: Token reflects the account's display name

- **WHEN** an authenticated user with a display name requests a token
- **THEN** the token's `firstName` claim equals that account's current display name

#### Scenario: Token never carries an email address

- **WHEN** an authenticated user requests a token
- **THEN** the token contains no `email` claim, regardless of what email address is on file for that account

#### Scenario: Account with no name is anonymous

- **GIVEN** a profile with no name set whose provider claim resembles an email address
- **WHEN** that user requests a token
- **THEN** `firstName` is the fixed generic fallback and neither that suggestion nor a provider value occurs in the token
