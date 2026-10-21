# Verification

Local verification on 2026-10-21 with .NET 10 and Docker Desktop Linux containers:

- Locked solution restore passed.
- `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` passed.
- `dotnet build TacticusPlanner.slnx -c Release --no-restore` passed. Existing AppHost ASPIRE010 bundle warning remains.
- `dotnet test TacticusPlanner.slnx -c Release --no-build --logger trx --results-directory TestResults` passed: 1,085 tests, zero failures, zero skips. API unit: 134; catalog: 380; persistence integration: 37; API integration: 534.
- TRX timestamps span 98.3 seconds for the whole solution. Reported integration execution durations: API 83 seconds; persistence 82 seconds. The reusable CI job retains its 15-minute budget and executes every solution project on `ubuntu-latest`, with no integration filters or skips. TRX/coverage artifacts upload even on failure.
- `docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .` passed.
- Both this change and `optimize-api-handlers` pass strict OpenSpec validation. Generated OpenAPI is unchanged.

The inventory in `test-inventory.json` accounts for all 522 original API test methods and their fact/theory/data attributes. The original API projects' 659 behavioral cases remain: 525 moved database-backed cases and 134 Docker-free cases (including three V1 parsing tests split from the mixed endpoint class). Nine new cases cover host lifecycle, external-state isolation, unavailable Docker, real transaction rollback, concurrent writes, and SQL measurement, giving 668 API cases in total.

Relational fixture repairs preserve original assertions: query-filter seeds supply required ordering and membership fields; imported user ids are unique; guild registration/read/sync/purge fixtures isolate constant-tag scenarios with per-test factories. The test clock supplies PostgreSQL-representable microsecond timestamps, preserving exact round-trip equality checks without changing production time behavior. External fake dictionaries and analytics bags are owned by each factory. Analytics configuration is cleared before production registrations run.

The rollback interceptor throws after real SQL has completed but before the surrounding transaction commits. HTTP regressions verify that failed guild refreshes retain no season/attack writes and failed imports retain no goals, memberships, or order revision changes. Goal-slot and legendary-event races retain their conflict responses and winning data. Request instrumentation executes all six sync/async command paths against PostgreSQL and verifies whole-request isolation and exclusion of setup/assertion SQL.
