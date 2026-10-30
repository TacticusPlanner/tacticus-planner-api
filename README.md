# Tacticus Planner API

ASP.NET Core API foundation for Tacticus Planner V2. The solution targets
.NET 10, uses PostgreSQL through EF Core, and uses .NET Aspire for local
orchestration and observability.

See [RELEASING.md](RELEASING.md) for the branching, versioning, backport,
container promotion, and coordinated apps/API release process.

## License and third-party rights

Original source code is licensed under the
[GNU Affero General Public License v3.0](LICENSE). See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the unofficial-project
disclaimer and third-party trademark, copyright, game-data, and asset rights.

## Prerequisites

- .NET SDK 10.0.402 or a newer 10.0 patch
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
| `RateLimiting__UpstreamFastPerMinute` | Optional, default `10`. Per-profile, per-endpoint requests/minute for `POST tacticus-api-key/validate` and `PUT me/tacticus-integration`; excess requests get `429` with `Retry-After` |
| `RateLimiting__UpstreamSlowPerHour` | Optional, default `6`. Per-profile, per-endpoint requests/hour for `POST tacticus-integration/player-sync`, `POST guilds/register`, `POST guilds/me/sync` and `POST me/v1-import` |
| `ColumnEncryption__CurrentKeyVersion` | Logical key version used to encrypt new/updated sensitive columns |
| `ColumnEncryption__Keys__<version>` | Base64/base64url 32-byte AES-256 key for the matching key version |
| `Analytics__ProjectToken` | PostHog project token (public — identifies the project, not a secret) |
| `Analytics__HostUrl` | PostHog ingestion host (public); must be `https://us.i.posthog.com` in every environment |
| `Analytics__IdentityKey` | **Secret.** Base64 32-byte HMAC key deriving each account's pseudonymous analytics id. Never rotate — rotating it re-keys every analytics identity |
| `ExternalIdentity__TenantId` | Entra External ID tenant id. With `ClientId`, enables deleting the caller's sign-in account on purge; empty keeps the no-op deleter |
| `ExternalIdentity__ClientId` | App registration (the Web API one) holding Microsoft Graph `User.DeleteRestore.All` (application, admin-consented) |
| `ExternalIdentity__ManagedIdentityClientId` | Client id of the user-assigned managed identity the app registration trusts through a federated credential (audience `api://AzureADTokenExchange`) |
| `ExternalIdentity__ClientSecret` | **Secret.** Fallback to the federated credential, e.g. for local development |
| `ExternalIdentity__PermanentlyDelete` | Default `true`: also removes the user from deleted items so the email can sign up again at once |
| `FeatureManagement__<Flag>` | `true` turns a feature flag on (see [Feature flags](#feature-flags)) |
| `AppConfiguration__Endpoint` | Production only. Azure App Configuration store holding the feature flags; empty (the default) reads flags from `appsettings*.json` and environment variables only |
| `AppConfiguration__ManagedIdentityClientId` | Client id of the user-assigned identity holding `App Configuration Data Reader` on the store |
| `AppConfiguration__RefreshInterval` | Optional, default `00:05:00`. How long flag values are cached; keeps the free tier's 1,000 requests/day |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Enables Azure Monitor telemetry |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Sends local telemetry to an OTLP collector |

`appsettings.Development.json` ships a fixed, throwaway `ColumnEncryption` key
so `dotnet run`/`dotnet test` work locally with no setup. It only ever
protects data in a local database and must never be reused outside
`Development`. Staging/production supply their own key(s) via deployment
configuration (Key Vault-backed), never through a committed `appsettings*.json`.

### Feature flags

Unfinished work merged to `main` stays hidden behind a feature flag
([Microsoft.FeatureManagement](https://learn.microsoft.com/azure/azure-app-configuration/feature-management-dotnet-reference)).
Every flag is off unless something turns it on:

- **Staging:** add `"<Flag>": true` under `FeatureManagement` in
  `appsettings.Staging.json`, in the same PR as the feature. Merging deploys it.
- **Production:** toggle the flag in the Azure App Configuration store
  (`appcs-tp-prod`, Operations → Feature manager). The API picks it up within
  `AppConfiguration:RefreshInterval` (5 minutes) without a restart. If the
  store is unreachable at startup, every flag reads as off.
- **Locally:** set `FeatureManagement__<Flag>=true` or add it to
  `appsettings.Development.json`.

Server code checks a flag with `IFeatureManager.IsEnabledAsync(...)`, or hides
a whole endpoint with `this.RequireFeature(FeatureFlags.X)` in `Configure()`
(404 while off). Name flags the API checks in `Features/FeatureFlags/FeatureFlags.cs`.
`GET /api/v1/feature-flags` (anonymous) lists the enabled flags for the web
app, so web-only flags need configuration only. Delete the flag, its checks
and its configuration once the feature ships.

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
- Authenticated user (auto-provisions the profile on first call): `GET /api/v1/me`
- Purge the authenticated user's account and all related data: `DELETE /api/v1/account` (add `?deleteSignInAccount=true` to also delete the Entra External ID user)
- Save/update the authenticated user's Tacticus integration: `PUT /api/v1/me/tacticus-integration`
- Sync player data from the Tacticus API: `POST /api/v1/me/player-data/sync`
- Import a Tacticus API key/user id from a V1 profile: `POST /api/v1/me/v1-import`

Goals and projects (the two lists are the whole planning read model):

| Route | Purpose |
| --- | --- |
| `GET /api/v1/me/goals[?archived=true]` | Every goal in full detail (config, snapshot, `projectIds`, `revision`, `globalPriority`) plus `orderRevision` |
| `GET /api/v1/me/goals/{goalId}` | One goal, the same shape as a list entry |
| `POST /api/v1/me/goals` | Create one goal, or several linked goals of one unit (`goals[]` with `dependsOnIndex`); request-level `startPaused` |
| `PUT /api/v1/me/goals/{goalId}` | The only edit route: optional `target`, `details`, `projectIds`, `priority` sections, all-or-nothing; answers `{ goal, order }` |
| `POST /api/v1/me/goals/status` | Transition one or many goals atomically (`{ goalIds, status, expectedOrderRevision }`) |
| `PUT /api/v1/me/goals/order` | Replace the in-flight order |
| `DELETE /api/v1/me/goals/{goalId}` | Delete a goal (unlinks it from dependents' `dependsOn`) |
| `GET /api/v1/me/projects[?archived=true]` | Projects, each with its member `goalIds` under the same archived selector |
| `PUT /api/v1/me/projects/{projectId}/goals` | Replace a project's membership (never slot-checked) |
| `GET /api/v1/me/legendary-event-plans/{eventId}/run-records?limit=&after=` | Run records, newest first, cursor-paged, without rosters |
| `GET /api/v1/me/legendary-event-plans/{eventId}/run-records/{recordId}/roster` | One record's roster snapshot (ETag/304) |

Every error is RFC 7807 `application/problem+json` with `issueCode` (and `errors` on 400, `current` on a 409
whose resource is known); see `Http/ApiProblems.cs`.

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

The schema is one `InitialCreate` migration (recreated pre-launch by `rebuild-foundation-pre-launch`). It is the EF
model plus exactly one raw statement appended to its `Up`: the deferrable
`ix_goals_profile_global_priority UNIQUE (profile_id, global_priority)` constraint, which the EF model cannot
express. `SchemaPostgresTests` fails if a future squash loses it. Database constraints are limited to keys, foreign
keys, uniqueness, nullability and `ck_goals_global_priority_in_flight`; range and enum rules live in validators.

The API always applies pending migrations before the server accepts requests.
This behavior is the same in Development, Staging, and Production and does not
require a configuration flag.

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

The API applies migrations automatically when Aspire starts it. The AppHost
also exposes an `api-migrations` resource whose `Update Database` command can
be used when migrations need to be applied without starting the API.

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
`42P07: relation "profiles" already exists`, the local database already has
tables but does not have matching EF migration history — for example a database
created by the migrations that `InitialCreate` replaced. For this greenfield
project, reset the local database instead of trying to preserve that state: use
the `api-migrations` resource `Reset Database` command in the Aspire Dashboard,
or run the `Drop Database` command followed by `Update Database`:

```powershell
aspire resource api-migrations drop-database --apphost orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj
aspire resource api-migrations update-database --apphost orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj
```

The first `GET /api/v1/me` against the fresh database provisions the caller's
profile and default project. Staging and production have no data to keep
pre-launch: `tacticus-planner-infra` drops and recreates their databases when
an image with a replaced migration history is deployed.

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

The test projects have separate responsibilities:

- `TacticusPlanner.Api.Tests`: pure validation/projection tests and database-independent catalog HTTP tests, including the manifest snapshot. No Docker required.
- `TacticusPlanner.Api.IntegrationTests`: database-backed HTTP behavior with fake authentication, Tacticus/V1 clients, and analytics. Requires Docker Desktop with Linux containers, or a Docker daemon on Linux.
- `TacticusPlanner.Persistence.IntegrationTests`: migrations, constraints, and direct persistence behavior. Requires Docker.
- `TacticusPlanner.GameCatalog.Tests`: catalog loading and denormalization. No Docker required.

Run the Docker-free projects independently:

```powershell
dotnet test tests/TacticusPlanner.Api.Tests
dotnet test tests/TacticusPlanner.GameCatalog.Tests
```

Verify `docker info` succeeds before running integration tests:

```powershell
dotnet test tests/TacticusPlanner.Api.IntegrationTests
dotnet test tests/TacticusPlanner.Persistence.IntegrationTests
```

The API integration assembly shares one disposable PostgreSQL 18 container. Each HTTP factory creates its own uniquely named database before startup. The production pooled Npgsql context, execution strategy, metadata interceptors, and migration hosted service are retained; requests begin only after migrations finish. Host disposal closes its connections and drops only its owned database; assembly teardown removes the container. Factories never reset shared tables or wrap requests in a rollback transaction. Missing Docker is a setup failure with an actionable message, never a skipped test or another provider.

Production persistence must follow the same PostgreSQL path in tests and deployment. Do not add provider switches or transaction/locking bypasses for tests. Configuration `AddInMemoryCollection` and external-boundary fakes are appropriate; replacing persistence is not.

The integration factory exposes `CommandsFor(response)`. A test-only response header correlates the request to the command interceptor. Counts include all EF reader, scalar, and nonquery execution attempts, synchronous or asynchronous, including retries, pre-processors, and handlers. Migration, seed, and assertion SQL outside requests is excluded. Transaction begin/commit operations are not SQL commands. Instrumentation is registered only by the integration test host.

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
local infrastructure deployment.

## Azure production deployment

Production ships from release tags, using the `Release` workflow (run by hand
from the Actions tab):

- From `main` with `minor` (or `major`): creates `release/X.Y` from `main`,
  tags `vX.Y.0`, creates a GitHub release with generated notes, and deploys
  the tag. The commit is already on `main`, so production reuses the exact
  image `CD Stage` built for it; check that commit's staging deploy succeeded
  before releasing. `CD Stage` skips documentation-only pushes, so for such a
  commit run `CD Stage` by hand first, or production builds a fresh image.
- From `release/X.Y` with `patch`: tags the next `vX.Y.Z` on that branch,
  creates its release (notes since the previous `vX.Y.*`), builds the image
  for that commit, and deploys it. Land the fix on `main` first, then
  cherry-pick it into the release branch with a PR (CI runs on PRs to
  `release/**`).

`CD Prod` does the deploy. `Release` calls it; run it by hand only to redeploy
an existing tag. Images are always built and pushed under the `stage`
environment (its identity holds `AcrPush` on the shared registry), so that
environment must allow `release/**` branches if it restricts deployment
branches. The update itself runs in the protected `production` environment,
which holds the same six variables as `stage` (set by
`pnpm infra -- access pre --env prod` in `tacticus-planner-infra`). Add
required reviewers to `production` so each release needs an approval, and
limit its deployment branches to `main` and `release/**`, so a workflow
changed on any other branch cannot use its Azure credentials.
Infrastructure changes, such as configuration or scaling, still go through
`tacticus-planner-infra`.

### Breaking contracts

Staging promotes every push to `main`. Merge a breaking API change and its web
client back to back; staging serves a mismatched pair until both deploys finish.
A replaced migration history also needs the staging database dropped and
recreated (`tacticus-planner-infra`) before the API deploys.
