# Verification

Local verification on 2026-10-21 with .NET 10 and Docker Desktop Linux containers:

- Locked solution restore passed.
- `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` passed.
- `dotnet build TacticusPlanner.slnx -c Release --no-restore` passed. Existing AppHost ASPIRE010 bundle warning remains.
- `dotnet test TacticusPlanner.slnx -c Release --no-build --logger trx --results-directory TestResults` passed: 1,085 tests, zero failures, zero skips. API unit: 134; catalog: 380; persistence integration: 37; API integration: 534.
- TRX timestamps span 98.3 seconds for the whole solution. Reported integration execution durations: API 83 seconds; persistence 82 seconds. The reusable CI job retains its 15-minute budget and executes every solution project on `ubuntu-latest`, with no integration filters or skips. TRX/coverage artifacts upload even on failure.
- `docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .` passed.
- Both this change and `optimize-api-handlers` pass strict OpenSpec validation. Generated OpenAPI is unchanged.

The inventory in `test-inventory.json` accounts for all 527 original API test methods and their fact/theory/data attributes. The original API projects' 659 behavioral cases remain: 525 moved database-backed cases and 134 Docker-free cases (including three V1 parsing tests split from the mixed endpoint class). Nine new cases cover host lifecycle, external-state isolation, unavailable Docker, real transaction rollback, concurrent writes, and SQL measurement, giving 668 API cases in total.

Relational fixture repairs preserve original assertions: query-filter seeds supply required ordering and membership fields; imported user ids are unique; guild registration/read/sync/purge fixtures isolate constant-tag scenarios with per-test factories. The test clock supplies PostgreSQL-representable microsecond timestamps, preserving exact round-trip equality checks without changing production time behavior. External fake dictionaries and analytics bags are owned by each factory. Analytics configuration is cleared before production registrations run.

The rollback interceptor throws after real SQL has completed but before the surrounding transaction commits. HTTP regressions verify that failed guild refreshes retain no season/attack writes and failed imports retain no goals, memberships, or order revision changes. Goal-slot and legendary-event races retain their conflict responses and winning data. Request instrumentation executes all six sync/async command paths against PostgreSQL and verifies whole-request isolation and exclusion of setup/assertion SQL.

GitHub CI run 39636197128 passed for implementation commit 3bdeb30 in 3 minutes 42 seconds for the build job, including locked restores, formatting, all tests with coverage, retained results, and the container build. Both CodeQL checks passed. CodeRabbit reviewed all committed implementation files and identified an inventory omission; the expanded audit corrected five missing method entries (theories with trailing comments), while all original theory cases were already preserved and passing.

Coverage follow-up: run 39637060750/job 119350414927 ran only API unit (134) and catalog (380) tests, while Build API job 119350378657 ran all 1,085 tests, including API integration (534) and persistence integration (37). The dedicated coverage workflow now explicitly runs and uploads all four projects, fails when any report is missing, and retains TRX/coverage artifacts even on failure.
