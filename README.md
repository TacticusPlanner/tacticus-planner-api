# Tacticus Planner API

ASP.NET Core API foundation for Tacticus Planner V2. The solution targets
.NET 10, uses PostgreSQL through EF Core, and uses .NET Aspire for local
orchestration and observability.

## Prerequisites

- .NET SDK 10.0.301 or a newer 10.0 patch
- Docker Desktop or another OCI-compatible container runtime
- The Aspire CLI, used to run and manage the local AppHost (see
  [Install the Aspire CLI](#install-the-aspire-cli) below)
- The .NET EF Core CLI tool when creating migrations:
  `dotnet tool install --global dotnet-ef --version 10.*`

### Install the Aspire CLI

```powershell
dotnet tool install -g Aspire.Cli
```

This is the recommended install path once the .NET 10 SDK is already present: it
produces a NativeAOT binary with instant startup and no JIT warmup. The
curl/PowerShell installer (`curl -sSL https://aspire.dev/install.sh | bash`) is
an equivalent alternative. After installing, verify the toolchain with:

```powershell
aspire doctor
```

`aspire doctor` diagnoses missing SDKs, container runtime issues, and other
environment problems before you try to start the AppHost.

## Restore and build

```powershell
dotnet restore src/TacticusPlanner.Api --locked-mode
dotnet restore orchestration/TacticusPlanner.AppHost
dotnet build -c Release --no-restore
```

Package versions are managed in `Directory.Packages.props`. Lock files are
committed for the API and ServiceDefaults projects. AppHost is intentionally
unlocked because Aspire injects platform-specific dashboard and orchestration
packages for Windows, Linux, or macOS during restore.

## Run the local full stack with Aspire

Aspire starts PostgreSQL, the API, the external React/Vite client app, and the
local observability dashboard. Use the Aspire CLI rather than `dotnet run` — it
manages the AppHost lifecycle correctly (locks, orphaned processes, and the
dashboard) instead of leaving a bare `dotnet` process behind:

```powershell
aspire run --project orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj
```

`aspire run` runs in the foreground and opens the dashboard, which is what you
want at an interactive terminal. Stop the stack with <kbd>Ctrl+C</kbd>, or with
`aspire stop` from another terminal.

If you need the AppHost running in the background instead (for example, while
scripting against it), use `aspire start` with the same `--project` argument,
and `aspire stop` to shut it down. Check what's running at any point with
`aspire ps` or `aspire describe`, rather than inspecting `docker ps` directly.

If a build fails with a file-lock error (`MSB3491`, `CS2012`) while Aspire is
running, it means Aspire still holds a lock on that project's build output —
run `aspire stop` first, then rebuild.

This integration is for local development only. It does not change staging or
production deployment, CI/CD workflows, or the client repository's standalone
Turborepo commands.

By default AppHost expects the API and client repositories to be checked out as
sibling folders:

```text
/tacticus/v2
  /tacticus-planner-api
  /tacticus-planner-apps
```

The default client app path is configured in
`orchestration/TacticusPlanner.AppHost/appsettings.json`:

```json
{
  "ClientAppPath": "../../../tacticus-planner-apps/apps/web"
}
```

Override `ClientAppPath` when your local checkout uses a different layout:

```powershell
$env:ClientAppPath = "D:\repos\tacticus\v2\tacticus-planner-apps\apps\web"
aspire run --project orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj
```

You can also store a machine-specific path in AppHost user secrets:

```powershell
dotnet user-secrets set "ClientAppPath" "D:\repos\tacticus\v2\tacticus-planner-apps\apps\web" --project orchestration/TacticusPlanner.AppHost
```

The `web` resource uses pnpm and the Vite `dev` script from the client app.
Because the client app is part of a Turborepo workspace, AppHost derives the
workspace root from `ClientAppPath` and runs the root `dev:web` Turbo script.
AppHost passes the API's local HTTP endpoint to the client as
`VITE_API_BASE_URL`, sets the web resource `PORT`, exposes the web resource at
the configured `WebPort` value, and configures the API CORS origin from the
Aspire-managed client endpoint.

The web resource defaults to `http://localhost:5173` through the `WebPort`
setting in `orchestration/TacticusPlanner.AppHost/appsettings.json`. Override
it when that port is already in use:

```powershell
$env:WebPort = "5174"
aspire run --project orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj
```

The local PostgreSQL resource uses fixed host port `51441` through the
`PostgresPort` setting in `orchestration/TacticusPlanner.AppHost/appsettings.json`.
Override it only when that port is already in use:

```powershell
$env:PostgresPort = "51442"
aspire run --project orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj
```

PostgreSQL uses a persistent container lifetime and a named Docker volume.
Stopping AppHost leaves the container available for the next run, and the
database data survives both AppHost and container restarts. Aspire supplies the
`planner-db` connection string to the API.

## Run the API directly

Set the PostgreSQL connection string before starting the API outside Aspire:

```powershell
$env:ConnectionStrings__planner-db = "Host=localhost;Port=5432;Database=tacticus_planner;Username=postgres;Password=<password>"
dotnet run --project src/TacticusPlanner.Api
```

The direct-development profile listens at `http://localhost:5100` and
`https://localhost:7100`.

## Configuration

Configuration is read from standard ASP.NET Core sources. Production values
must be supplied through environment configuration or the deployment platform;
do not commit secrets.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__planner-db` | PostgreSQL connection string |
| `Authentication__Authority` | Microsoft Entra External ID token authority |
| `Authentication__Audience` | Exact access-token `aud` claim for the API |
| `Cors__AllowedOrigins__0` | First allowed frontend origin; add more by index |
| `TacticusApi__BaseUrl` | Base URL of the upstream Tacticus game API used to validate personal API keys |
| `V1Api__BaseUrl` | Base URL of the legacy V1 planner backend used by V1 profile import |
| `V1Api__FunctionsKey` | Azure Functions `x-functions-key` for the V1 backend's `LoginUser`/`GetUserData` HTTP triggers (`AuthorizationLevel.Function`); sent as a header on every V1 request when set |
| `ColumnEncryption__CurrentKeyVersion` | Logical key version used to encrypt new/updated sensitive columns |
| `ColumnEncryption__Keys__<version>` | Base64/base64url 32-byte AES-256 key for the matching key version |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Enables Azure Monitor telemetry |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Sends local telemetry to an OTLP collector |

`appsettings.Development.json` ships a fixed, throwaway `ColumnEncryption` key
so `dotnet run`/`dotnet test` work locally with no setup. It only ever
protects data in a local database and must never be reused outside
`Development`. Staging/production supply their own key(s) via deployment
configuration (Key Vault-backed), never through a committed `appsettings*.json`.

Use user-secrets for local identity values:

```powershell
dotnet user-secrets set "Authentication:Authority" "<ciam-authority>" --project src/TacticusPlanner.Api
dotnet user-secrets set "Authentication:Audience" "<aud-claim-from-local-api-access-token>" --project src/TacticusPlanner.Api
```

Local development uses the dedicated local identity registrations and API
audience while calling the locally hosted API. For Microsoft Entra v2.0 access
tokens, the API audience is the API app registration's application/client ID
GUID. Do not use the delegated scope, such as
`api://tacticus-planner-api-local/access_as_user`, as
`Authentication:Audience`.

Authentication is the default authorization policy. Health and OpenAPI
endpoints are intentionally anonymous. The frontend must request the deployed
API's `access_as_user` scope before calling protected endpoints. Routes under
`/api/v1` enforce that delegated scope in addition to validating the token's
issuer and audience.

## API and health endpoints

- OpenAPI JSON: `/openapi/v1.json`
- Interactive API reference in Development: `/docs`
- Liveness: `/health/live`
- Readiness, including PostgreSQL: `/health/ready`
- Authenticated user (auto-provisions the account/profile on first call): `GET /api/v1/me`
- Purge the authenticated user's account and all related data: `DELETE /api/v1/me`
- Save/update the authenticated user's Tacticus integration: `PUT /api/v1/me/tacticus-integration`
- Import a Tacticus API key/user id from a V1 profile: `POST /api/v1/me/v1-import`
- Validate a Tacticus API key without persisting it: `POST /api/v1/tacticus-api-key/validate`

Future feature endpoints belong under `/api/v1` and require the delegated
`access_as_user` scope by default. Tacticus API keys, Tacticus user ids, and V1 credentials are never returned to
the client in full — only masked previews (see `SecretMasker`) or onboarding-completion flags. V1 usernames and
passwords are used once, in-request, to acquire a V1 access token and are never persisted.

Every API build generates the OpenAPI artifact under `artifacts/openapi`:

```powershell
dotnet build src/TacticusPlanner.Api -c Release --no-restore
```

## Database migrations

Create migrations when the persisted model changes:

```powershell
dotnet ef migrations add <MigrationName> --project src/TacticusPlanner.Persistence --startup-project src/TacticusPlanner.Api
```

The API applies pending migrations automatically during startup when
`ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT` is `Staging` or `Production` and
`Database__ApplyMigrationsOnStartup=true`. Local `Development` runs do not
apply migrations automatically.

`appsettings.Development.json` contains a placeholder local connection string:

```json
"ConnectionStrings": {
  "planner-db": "Host=localhost;Port=51441;Username=postgres;Password=postgres-admin;Database=tacticus_planner"
}
```

For local manual execution through Aspire, start the AppHost:

```powershell
aspire run --project orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj
```

Then open the Aspire Dashboard, find the `api-migrations` resource, and run
the `Update Database` command from its actions menu. The AppHost registers this
manual migration resource without running migrations on startup and without
making the API wait for the migration resource to complete.

You can also invoke the resource command from the CLI:

```powershell
aspire resource api-migrations update-database --apphost orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj
```

Aspire configures the local PostgreSQL host port as `51441` and password as
`postgres-admin` unless overridden. Docker may still show an internal random
proxy port, but local tools should use `localhost:51441`.

[pgAdmin](https://www.pgadmin.org/) can be used as a GUI for the local
PostgreSQL instance with the same host, port, username, password, and database.

If the local Aspire PostgreSQL volume already existed before this password was
configured, PostgreSQL keeps the original password stored in the data volume.
Either use the password shown in the Aspire dashboard for that existing
resource, change the database password manually, or reset the local
`tacticus-planner-postgres-data` Docker volume and let Aspire recreate it.

If `Update Database` fails with an error such as
`42P07: relation "accounts" already exists`, the local database already has
tables but does not have matching EF migration history. For this greenfield
project, reset the local database instead of trying to preserve that partial
state: use the `api-migrations` resource `Reset Database` command in the
Aspire Dashboard, or run the `Drop Database` command followed by
`Update Database`.

## Container image

Build the same API image shape intended for Azure Container Apps:

```powershell
docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .
```

The image runs as a non-root user and listens on port `8080`. Aspire is only a
local development dependency and is not included in the runtime image.

## Validation

```powershell
dotnet restore src/TacticusPlanner.Api --locked-mode
dotnet restore orchestration/TacticusPlanner.AppHost
dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore
dotnet build TacticusPlanner.slnx -c Release --no-restore
dotnet test TacticusPlanner.slnx -c Release --no-build
docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .
```

`tests/TacticusPlanner.Api.Tests` covers the account/Tacticus-integration/V1-import/purge endpoints against an
EF Core InMemory-backed `PlannerDbContext` and fake Tacticus/V1 clients (no live PostgreSQL or outbound network
calls required), plus a snapshot test for the public game catalog manifest. Run it with:

```powershell
dotnet test TacticusPlanner.slnx --no-build
```

## Azure staging deployment

The `CD Stage` workflow builds and pushes an immutable image after each merge to
`main`, then updates the staging Container App when it exists. Azure login uses
GitHub OIDC; no Azure client secret or PostgreSQL password is stored in this
repository.

Configure the repository's protected `stage` environment with:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `ACR_NAME`
- `CONTAINER_APP_NAME`
- `RESOURCE_GROUP_NAME`

The first workflow run may occur before the Container App is provisioned. In
that case it publishes the image, reports the immutable digest in the workflow
summary, and skips deployment successfully. Supply that digest to the initial
local infrastructure deployment. Subsequent runs update the existing Container
App automatically.
