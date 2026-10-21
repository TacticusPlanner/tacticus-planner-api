# Repository Guidelines

## Project Structure & Module Organization

ASP.NET Core (.NET 10) API for Tacticus Planner V2, using EF Core + PostgreSQL
and .NET Aspire for local orchestration.

- `src/TacticusPlanner.Api`: the ASP.NET Core web API (endpoints, DI wiring,
  auth policies). Generates the OpenAPI artifact under `artifacts/openapi` on
  every build.
- `src/TacticusPlanner.Domain`: domain model shared across the API.
- `src/TacticusPlanner.Persistence`: EF Core `DbContext`, entity
  configuration, and migrations (`dotnet ef migrations add ...` targets this
  project).
- `src/TacticusPlanner.GameCatalog`: server-side game catalog — embedded raw
  datasets, runtime denormalization into served datasets, hashing/manifest.
  See the `game-catalog-data` skill before adding/changing catalog data.
- `src/TacticusPlanner.TacticusApi`: client for the upstream Tacticus game API
  (used to validate personal API keys).
- `tests/TacticusPlanner.Api.Tests`: Docker-free pure tests and database-independent catalog HTTP tests.
- `tests/TacticusPlanner.Api.IntegrationTests`: database-backed endpoint tests with production pooled Npgsql, migrations, and external-boundary fakes. Shares a disposable PostgreSQL container; each factory owns an isolated database. Requires Docker.
- `tests/TacticusPlanner.GameCatalog.Tests`: catalog manifest/denormalization tests.
- `tests/TacticusPlanner.Persistence.IntegrationTests`: Docker-backed PostgreSQL migration, relational-constraint, and direct persistence tests.
- `orchestration/TacticusPlanner.AppHost`: .NET Aspire AppHost for local dev —
  starts PostgreSQL, the API, and (via `ClientAppPath`) the external
  `tacticus-planner-apps` React/Vite client. `orchestration/TacticusPlanner.ServiceDefaults`
  holds shared Aspire service defaults (telemetry, health checks).
- `.agents/skills` (canonical) / `.claude/skills` (mirror): local agent skill
  docs — `game-catalog-data` and `vulnerabilities-scan` (NuGet vulnerability
  scan + fix process for this repo).

## Build, Test, and Development Commands

```powershell
dotnet restore src/TacticusPlanner.Api --locked-mode
dotnet restore orchestration/TacticusPlanner.AppHost
dotnet build TacticusPlanner.slnx -c Release --no-restore
dotnet test TacticusPlanner.slnx -c Release --no-build
dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore
```

- Package versions are centrally managed in `Directory.Packages.props`. Lock
  files are committed for the API and ServiceDefaults projects; AppHost is
  intentionally unlocked (Aspire injects platform-specific packages).
- Run the local full stack with **Aspire, not `dotnet run`**:
  `aspire run --project orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj`.
  Use `aspire stop`/`aspire ps`/`aspire describe` to manage it — see
  `README.md` for the `ClientAppPath`, `WebPort`, and `PostgresPort`
  overrides and the AppHost's sibling-folder assumption (satisfied by this
  workspace's flat submodule layout: `tacticus-planner-apps` is a direct
  sibling of `tacticus-planner-api`).
- Run the API directly (outside Aspire) with `dotnet run --project src/TacticusPlanner.Api`
  after setting `ConnectionStrings__planner-db`.
- Install the Aspire CLI (`dotnet tool install -g Aspire.Cli`) and EF Core CLI
  (`dotnet tool install --global dotnet-ef --version 10.*`) once per machine;
  verify the toolchain with `aspire doctor`.

## Coding Style & Naming Conventions

C# via `.editorconfig`: 4-space indent, file-scoped namespaces (warning if
not), `dotnet_sort_system_directives_first`, unused-usings as a build warning
(`IDE0005`). Non-C# files (`*.csproj`, `*.props`, `*.slnx`) use 2-space indent.
Run `dotnet format TacticusPlanner.slnx` before committing; CI verifies with
`--verify-no-changes`.

## Testing Guidelines

`dotnet test TacticusPlanner.slnx --no-build` runs all test projects and requires Docker. Run `dotnet test tests/TacticusPlanner.Api.Tests` and `dotnet test tests/TacticusPlanner.GameCatalog.Tests` for Docker-free coverage. Run `dotnet test tests/TacticusPlanner.Api.IntegrationTests` for PostgreSQL HTTP behavior and `dotnet test tests/TacticusPlanner.Persistence.IntegrationTests` for migrations and direct persistence. Neither API test project may call live upstream services or analytics.

Keep production persistence identical in tests and deployment: no alternate providers or test-driven transaction/locking bypasses. Seed complete relational graphs instead of weakening constraints. The integration host retains production pooling, execution strategy, interceptors, and migration startup. See README for lifecycle, isolation, and Docker troubleshooting.

New catalog data/denormalization logic belongs in `TacticusPlanner.GameCatalog.Tests`, including the public catalog manifest snapshot when its shape changes.

## Configuration & Security

Configuration comes from standard ASP.NET Core sources (`appsettings*.json`,
environment, user-secrets) — see the full settings table in `README.md`.
Never commit secrets: `appsettings.Development.json` ships a throwaway
`ColumnEncryption` key for local dev only; staging/production keys come from
Key Vault-backed deployment configuration. Tacticus API keys, Tacticus user
ids, and V1 credentials are never returned to the client in full (see
`SecretMasker`) and V1 username/password are used once in-request, never
persisted. Authentication is the default authorization policy; `/health/*`
and OpenAPI/`/docs` endpoints are intentionally anonymous; routes under
`/api/v1` require the delegated `access_as_user` scope.

## Database Migrations

```powershell
dotnet ef migrations add <MigrationName> --project src/TacticusPlanner.Persistence --startup-project src/TacticusPlanner.Api
```

The API always applies pending migrations on startup (Development, Staging,
and Production alike). The history is a single `InitialCreate` (pre-launch reset)
plus whatever follows it; its `Up` ends with the one raw statement the EF model
cannot express — the deferrable `ix_goals_profile_global_priority` unique
constraint — and any squash must keep it (`SchemaPostgresTests` guards it). Keep
database constraints to keys, foreign keys, uniqueness and nullability (plus
`ck_goals_global_priority_in_flight`); put range/enum invariants in validators.
Mark nothing under `Migrations/` by hand: it is generated code (see its
`.editorconfig`). Through Aspire, use the `api-migrations` resource's
`Update Database`/`Reset Database`/`Drop Database` commands (dashboard or
`aspire resource api-migrations <command> --apphost ...`) instead of manual
SQL against the local database.

## Commit & Pull Request Guidelines

This repository currently tracks work on the `svehera/goals-phase-9` branch,
not `main` — open PRs against that branch unless told otherwise. Any new
endpoint or protocol change to an existing endpoint must be reflected in the
generated OpenAPI artifact (produced automatically on build) and, if the
frontend consumes it, coordinated with `tacticus-planner-apps`. A breaking
contract ships with automatic staging promotion off (`STAGE_AUTO_DEPLOY`) and is
promoted together with its client — see README "Coordinated cutover".

Validation before opening a PR:

```powershell
dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore
dotnet build TacticusPlanner.slnx -c Release --no-restore
dotnet test TacticusPlanner.slnx -c Release --no-build
docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .
```
