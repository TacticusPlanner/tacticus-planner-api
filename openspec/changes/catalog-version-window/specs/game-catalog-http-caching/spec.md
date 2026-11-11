# Spec Delta

## ADDED Requirements

### Requirement: Dataset URLs resolve within the served releases

A bare `GET /api/v1/game-catalog/{dataset}` SHALL serve the `Latest` release's envelope. A content-addressed `GET /api/v1/game-catalog/{dataset}/{envelopeHash}` SHALL serve the envelope with that hash from whichever served release has it, with the immutable cache headers it carries today. A hash that no served release has SHALL answer `404 Not Found`. Each manifest SHALL list URLs of its own release's envelopes only.

#### Scenario: A previous-release URL resolves

- **GIVEN** the `1.43.157.2989` manifest lists `quest-events` at `/api/v1/game-catalog/quest-events/<h43>`
- **WHEN** a client fetches that URL while `1.43.157.2989` is `Previous`
- **THEN** it receives the `1.43.157.2989` quest-events envelope, whose `GameVersion` is `1.43.157.2989`, with `Cache-Control: public, max-age=31536000, immutable`

#### Scenario: Releases with identical data still have separate envelopes

- **GIVEN** `factions` data is identical in both releases
- **WHEN** both manifests are read
- **THEN** the two `factions` entries have the same `hash` but different URLs, because each envelope carries its own release's `Version` and `GameVersion`

#### Scenario: A URL from a release that left the window

- **WHEN** a client fetches a content-addressed URL whose envelope belonged only to a release that has left the window
- **THEN** the API answers `404 Not Found`

#### Scenario: The bare route stays latest

- **WHEN** a client requests `GET /api/v1/game-catalog/characters` without a hash
- **THEN** it receives the `Latest` release's envelope
