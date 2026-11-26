## ADDED Requirements

### Requirement: Token carries non-identifying feedback context as traits

The token SHALL include a `traits` claim whose value is a flat JSON object of scalar values. It SHALL always contain `tacticusApiLinked`, a boolean that is true when the account has a Tacticus API key linked. The endpoint SHALL accept optional `appVersion`, `appMode` and `language` query parameters and SHALL copy each into `traits` under the same name only when it is well formed: `appVersion` is 1 to 40 characters of letters, digits, `.`, `+`, `_` or `-`, starting with a letter or digit; `appMode` is `installed` or `browser`; `language` is a lowercase two- or three-letter language code with an optional uppercase two-letter region (`en`, `pt-BR`). A missing or malformed parameter SHALL be left out of `traits` without failing the request. `traits` SHALL NOT contain the Tacticus API key, a Tacticus user id, an email address or any other contact information.

#### Scenario: Well-formed context is carried

- **WHEN** an authenticated user requests a token with `appVersion=v2.6.0`, `appMode=installed` and `language=pt-BR`
- **THEN** the token's `traits` contains those three values and `tacticusApiLinked`

#### Scenario: Malformed context is dropped, not rejected

- **WHEN** an authenticated user requests a token with an `appVersion` containing markup, `appMode=tablet` and `language=english`
- **THEN** the response is a successful token whose `traits` contains only `tacticusApiLinked`

#### Scenario: A request without context still gets traits

- **WHEN** an authenticated user requests a token with no query parameters
- **THEN** the token's `traits` contains only `tacticusApiLinked`

#### Scenario: A linked key is reported without the key

- **GIVEN** an account with a Tacticus API key linked
- **WHEN** that user requests a token
- **THEN** `traits.tacticusApiLinked` is true and the key does not occur anywhere in the token
