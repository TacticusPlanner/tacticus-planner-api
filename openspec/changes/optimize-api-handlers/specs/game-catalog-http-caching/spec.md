## Purpose

Served game-catalog datasets are content-addressed and public, so clients and intermediaries can revalidate them cheaply and every API response travels compressed.

## ADDED Requirements

### Requirement: Dataset responses carry a content ETag

Every `GET game-catalog/{dataset}` response SHALL carry an `ETag` derived from that dataset's content hash (the same hash the manifest lists for it) and a `Cache-Control` header that permits caching but requires revalidation. Two responses for the same dataset content SHALL carry the same `ETag`; a changed dataset SHALL carry a different one.

#### Scenario: ETag matches the manifest hash

- **WHEN** a client fetches `game-catalog/manifest` and then `game-catalog/characters`
- **THEN** the dataset response's `ETag` corresponds to the manifest's hash for `characters` and the response carries `Cache-Control`

#### Scenario: Content change changes the ETag

- **GIVEN** a dataset response with `ETag` E1
- **WHEN** the served catalog is replaced by one whose `characters` content differs
- **THEN** the next `characters` response carries an `ETag` different from E1

### Requirement: Dataset requests honour If-None-Match

A `GET game-catalog/{dataset}` request whose `If-None-Match` matches the current dataset `ETag` SHALL receive `304 Not Modified` with no body and the current `ETag`; a non-matching or absent `If-None-Match` SHALL receive `200` with the full envelope.

#### Scenario: Unchanged dataset

- **WHEN** a client sends `If-None-Match` equal to the current `ETag` of `game-catalog/upgrades`
- **THEN** the response is 304 with an empty body and the same `ETag`

#### Scenario: Stale validator

- **WHEN** a client sends an `If-None-Match` that does not match the current `ETag`
- **THEN** the response is 200 with the full dataset envelope and the current `ETag`

### Requirement: API responses are compressed on request

When a request's `Accept-Encoding` includes `br` or `gzip`, any JSON response larger than a small threshold SHALL be sent with that encoding (Brotli preferred) and a matching `Content-Encoding` header, over both HTTP and HTTPS. Responses SHALL vary on `Accept-Encoding`.

#### Scenario: Catalog dataset over HTTPS

- **WHEN** a client requests `game-catalog/campaign-battles` with `Accept-Encoding: br, gzip`
- **THEN** the response carries `Content-Encoding: br` and `Vary: Accept-Encoding`, and decoding yields the same JSON as an uncompressed request

#### Scenario: Client without compression support

- **WHEN** a client requests any endpoint without `Accept-Encoding`
- **THEN** the response is uncompressed and carries no `Content-Encoding`
