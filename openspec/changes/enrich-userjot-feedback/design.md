## Context

`GET /api/v1/userjot-token` signs an HS256 JWT for UserJot's widget (`sub`, `iss`, `aud`, `iat`, `exp`, `firstName`). UserJot's signed-identity contract also accepts optional `traits` (flat scalars; nested objects and arrays are ignored). The companion apps change is `enrich-userjot-feedback`.

## Decisions

- **Client context travels as query parameters and is echoed into the signed token**, rather than sent unsigned beside the token. The widget runs signed identity only (`userjot-widget`), and a token value wins over an unsigned one anyway. The values are the caller's own context (build, display mode, language), so signing what the browser reports does not let anyone claim another identity; it only means the trait is "as reported by the client".
- **Strict shapes, drop on mismatch.** `appVersion` must look like a tag, version or SHA (`^[0-9A-Za-z][0-9A-Za-z.+_-]{0,39}$`), `appMode` is `installed` or `browser`, `language` is a BCP 47 primary tag with optional region (`^[a-z]{2,3}(-[A-Z]{2})?$`). A mismatch drops that trait and still returns 200, so the deployed client (no parameters) and any odd value never break the widget.
- **`tacticusApiLinked` is derived on the server** from the profile's Tacticus integration (key present), read in the same query as the display name. It is always present, so `traits` is never empty.
- **No migration.** Nothing is stored; the values exist only in the short-lived token.

## Risks

- UserJot may later reserve a trait key; the SDK reports `RESERVED_TRAIT_KEY_IGNORED` and drops just that key, leaving identity intact.
