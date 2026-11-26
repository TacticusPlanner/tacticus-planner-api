## 1. Token traits

- [x] 1.1 Add `UserJotTraits.Build` with the strict shapes for `appVersion`, `appMode` and `language`
- [x] 1.2 Accept the three optional query parameters on `GET /api/v1/userjot-token` and sign a flat `traits` claim
- [x] 1.3 Derive `browser` and `os` traits from the `User-Agent` header with `CoarseUserAgent`
- [x] 1.4 Regenerate the OpenAPI artifact (three optional query parameters)

## 2. Tests

- [x] 2.1 Unit tests for trait shapes and the signed `traits` claim being a flat object
- [x] 2.2 Integration tests: well-formed context is echoed, malformed context is dropped with 200 and no `traits` claim, a `User-Agent` is reduced to browser and OS without the full header
