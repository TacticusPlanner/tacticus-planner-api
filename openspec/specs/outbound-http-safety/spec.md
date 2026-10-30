# outbound-http-safety Specification

## Purpose
Safety bounds on the API's outbound HTTP clients: which requests are retried, and the redirect and response-size limits on the V1 backend client.

## Requirements

### Requirement: Unsafe upstream requests are not retried

The default outbound HTTP resilience handler SHALL retry only safe methods (GET, HEAD, OPTIONS, TRACE). A POST, PUT, PATCH or DELETE that times out or receives a 5xx SHALL NOT be sent again.

#### Scenario: Timed-out V1 login

- **WHEN** an outbound POST times out
- **THEN** it is sent exactly once

### Requirement: The V1 client is bounded

The V1 API client SHALL NOT follow redirects, because it sends the functions key on every request. It SHALL buffer at most 16 MB of response body.

When a V1 profile read fails, `POST account/v1-import` SHALL respond with the existing 400 naming `username` ("The V1 profile could not be retrieved."). This covers a non-success status, a body over the limit and a transport failure. It SHALL NOT respond with a 500.

#### Scenario: Oversized V1 profile

- **GIVEN** V1 `users/me` returns a body larger than 16 MB
- **WHEN** the caller sends `POST account/v1-import`
- **THEN** the response is 400 and nothing is imported

#### Scenario: Redirect

- **GIVEN** a V1 endpoint answers with a redirect
- **WHEN** the client calls it
- **THEN** the redirect is not followed and the functions key is not sent to the redirect target
