# health-endpoints Specification

## Purpose
The anonymous liveness and readiness endpoints and how readiness checks are cached so anonymous callers cannot drive database load.

## Requirements

### Requirement: Readiness results are shared briefly

`/health/ready` SHALL stay anonymous and run every health check. It SHALL serve one cached result, healthy or not, to all callers for 5 seconds. The cache key SHALL ignore the query string, the `Host` header and the `Authorization` header, so a caller cannot force a fresh database round-trip. `/health/live` SHALL NOT be cached.

#### Scenario: Repeated readiness probes

- **WHEN** two requests to `/health/ready?x=1` and `/health/ready?x=2` arrive within 5 seconds
- **THEN** the health checks run once and both receive the same result
