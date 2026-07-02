# Tacticus Planner API

ASP.NET Core API foundation for Tacticus Planner V2. The solution targets
.NET 10, uses PostgreSQL through EF Core, and uses .NET Aspire for local
orchestration and observability.

## Prerequisites

- .NET SDK 10.0.301 or a newer 10.0 patch
- Docker Desktop or another OCI-compatible container runtime
- The .NET EF Core CLI tool when creating migrations:
  `dotnet tool install --global dotnet-ef --version 10.*`

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
local observability dashboard:

```powershell
dotnet run --project orchestration/TacticusPlanner.AppHost
```

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
dotnet run --project orchestration/TacticusPlanner.AppHost
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
dotnet run --project orchestration/TacticusPlanner.AppHost
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
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Enables Azure Monitor telemetry |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Sends local telemetry to an OTLP collector |

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
- Authenticated user: `/api/v1/me`

Future feature endpoints belong under `/api/v1` and require the delegated
`access_as_user` scope by default.

Every API build generates the OpenAPI artifact under `artifacts/openapi`:

```powershell
dotnet build src/TacticusPlanner.Api -c Release --no-restore
```

## Database migrations

Create migrations when the persisted model changes:

```powershell
dotnet ef migrations add <MigrationName> --project src/TacticusPlanner.Api
```

The API applies pending migrations automatically during startup when
`ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT` is `Staging` or `Production` and
`Database__ApplyMigrationsOnStartup=true`. Local `Development` runs do not
apply migrations automatically; apply them explicitly when needed:

```powershell
dotnet ef database update --project src/TacticusPlanner.Api
```

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
docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .
```

No automated test project is configured in this foundation.

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
