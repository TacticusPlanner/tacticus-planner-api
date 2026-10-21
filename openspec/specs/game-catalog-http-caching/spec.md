# game-catalog-http-caching Specification

## Purpose

Served game-catalog datasets are content-addressed and public, so clients and intermediaries can revalidate them cheaply and every API response travels compressed.

## Requirements

### Requirement: Dataset responses carry a content ETag

Every dataset response SHALL carry a strong `ETag` covering the complete served envelope (`Version`, `SchemaVersion`, `GameVersion`, `SourceHash`, `DatasetKey`, `DatasetHash`, `Data`). The manifest payload hash alone SHALL NOT determine this validator. Equal complete representations SHALL have equal validators; changes to data or release metadata SHALL change it.

#### Scenario: Envelope and manifest describe the same snapshot

- **WHEN** a client fetches `game-catalog/manifest` and then `game-catalog/characters` from one catalog snapshot
- **THEN** the envelope's dataset hash matches the manifest's `characters` hash, and the response ETag covers that envelope's data and release metadata

#### Scenario: Metadata-only release invalidates the cached envelope

- **GIVEN** a cached dataset response whose payload hash stays unchanged across a release
- **WHEN** the new release changes `SourceHash` or `Version` and the client revalidates with the old ETag
- **THEN** the response is 200 with the new envelope metadata and a different ETag

#### Scenario: Content change changes the ETag

- **GIVEN** a dataset response with `ETag` E1
- **WHEN** the served catalog is replaced by one whose `characters` content differs
- **THEN** the next `characters` response carries an `ETag` different from E1

### Requirement: Cache headers distinguish encoding variants

Strong validators SHALL distinguish identity, Brotli, and gzip representations of the same envelope. Responses SHALL carry `Vary: Accept-Encoding` and `Cache-Control: public, max-age=0, must-revalidate`.

#### Scenario: Content-encoding variants have distinct strong validators

- **WHEN** the same envelope is served as identity, Brotli, and gzip
- **THEN** each encoded representation has its own strong ETag and `Vary: Accept-Encoding`


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
