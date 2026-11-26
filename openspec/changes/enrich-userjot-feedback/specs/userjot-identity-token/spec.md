## ADDED Requirements

### Requirement: Token carries non-identifying feedback context as traits

When it has any feedback context, the token SHALL include a `traits` claim whose value is a flat JSON object of scalar values; with none, the token SHALL NOT include a `traits` claim. The endpoint SHALL accept optional `appVersion`, `appMode` and `language` query parameters and SHALL copy each into `traits` under the same name only when it is well formed: `appVersion` is 1 to 40 characters of letters, digits, `.`, `+`, `_` or `-`, starting with a letter or digit; `appMode` is `installed` or `browser`; `language` is a lowercase two- or three-letter language code with an optional uppercase two-letter region (`en`, `pt-BR`). A missing or malformed parameter SHALL be left out of `traits` without failing the request. The endpoint SHALL also add `browser`, a recognised browser family with its major version (such as `Chrome 131`), and `os`, a recognised operating system family (such as `Android`), derived from the request's `User-Agent` header; an unrecognised header adds neither. The full `User-Agent` header SHALL NOT appear in the token. `traits` SHALL NOT contain the Tacticus API key, a Tacticus user id, an email address or any other contact information.

#### Scenario: Well-formed context is carried

- **WHEN** an authenticated user requests a token with `appVersion=v2.6.0`, `appMode=installed` and `language=pt-BR`
- **THEN** the token's `traits` contains those three values

#### Scenario: Malformed context is dropped, not rejected

- **WHEN** an authenticated user requests a token with an `appVersion` containing markup, `appMode=tablet` and `language=english`
- **THEN** the response is a successful token with no `traits` claim

#### Scenario: A request without context has no traits

- **WHEN** an authenticated user requests a token with no query parameters
- **THEN** the token has no `traits` claim

#### Scenario: The user agent is reduced to browser and OS families

- **WHEN** an authenticated user on Chrome 131 for Android requests a token
- **THEN** the token's `traits` contains `browser` `Chrome 131` and `os` `Android`, and the full `User-Agent` header does not occur in the token
