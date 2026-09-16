## 1. Dependency and configuration

- [x] 1.1 Add `PostHog.AspNetCore` to `Directory.Packages.props` and reference it from `TacticusPlanner.Api`; verify `dotnet restore src/TacticusPlanner.Api --locked-mode` succeeds with the refreshed lock file committed
- [x] 1.2 Add the `Analytics` section (`ProjectToken`, `HostUrl`, `IdentityKey`) to `appsettings.json` with `HostUrl` set to `https://us.i.posthog.com` and no token or key value committed; verify the app still starts locally with `Analytics:IdentityKey` supplied via user-secrets
- [x] 1.3 Document the `Analytics` settings in `README.md`'s configuration table alongside the existing `UserJot` entries; verify the table lists which values are secret and which are public

## 2. Analytics identity

- [x] 2.1 Create `Features/Analytics/AnalyticsOptions.cs` binding the `Analytics` section, with `IdentityKey` validated as a non-empty 32-byte key and `ProjectToken` explicitly optional; verify a missing `IdentityKey` fails host startup and a missing `ProjectToken` does not
- [x] 2.2 Implement the analytics id deriver as `lowercase_hex(HMAC-SHA256(IdentityKey, "<destination>:" + accountId))` with `posthog` as a compile-time destination constant; verify unit tests cover determinism, distinctness across accounts, distinctness across destinations, and that the output contains no encoding of the account id
- [x] 2.3 Add a unit test asserting that ids derived for accounts created in a known order do not sort in that order, covering the v7-timestamp disclosure requirement in `specs/analytics-identity/spec.md`
- [x] 2.4 Add `AnalyticsId` to `CurrentUserResponse` and populate it in `GetCurrentUserEndpoint`; verify an endpoint test asserts the returned value equals the independently derived id for that caller

## 3. Capture abstraction

- [x] 3.1 Define `IProductAnalytics` with one typed method per declared event (`AccountRegistered`, `GuildRegistered`) and no free-form property-bag overload; verify the interface exposes no way to pass arbitrary properties
- [x] 3.2 Implement the vendor-backed adapter over `PostHog.AspNetCore`, non-awaiting on the request path and with no per-request flush; verify it is the only file in the solution importing the PostHog SDK
- [x] 3.3 Implement the inert no-op implementation selected when `Analytics:ProjectToken` is absent; verify a test booting without a token performs an event-emitting operation and makes no outbound request
- [x] 3.4 Create `Features/Analytics/DependencyInjection.cs` with `AddAnalyticsFeature(configuration, validateOnStart)` and register it in `Program.cs`, following `AddUserJotFeature`'s `isOpenApiDocumentGeneration` handling; verify `dotnet build` regenerates the OpenAPI artifact without startup failure

## 4. Event emission

- [x] 4.1 Emit `account_registered` from `GetCurrentUserEndpoint.ProvisionAccountAsync`; verify endpoint tests assert it fires on first access, does not fire for an already-provisioned caller, and does not fire when provisioning fails
- [x] 4.2 Add a "guild was newly created" flag to `GuildSyncResult.Success`, set by `GuildSyncService.SynchronizeAsync` where the `Guild` row is created; verify existing guild sync and registration tests still pass
- [x] 4.3 Emit `guild_registered` from `RegisterGuildEndpoint` only when that flag is set; verify endpoint tests assert it fires on first registration, does not fire on re-registration of an already-registered guild, and does not fire when registration is rejected
- [x] 4.4 Add a test asserting no emitted event carries an account id, display name, email, Tacticus API key, Tacticus user id, or guild name, covering the privacy floor in `specs/product-analytics-events/spec.md`

## 5. Test harness

- [x] 5.1 Add a recording fake `IProductAnalytics` to `TacticusPlanner.Api.Tests` and swap it in `PlannerApiFactory.ConfigureTestServices` alongside the existing `ITacticusApi`/`ITacticusV1Client` fakes; verify captured events are assertable from tests
- [x] 5.2 Supply a fixed `Analytics:IdentityKey` and no `Analytics:ProjectToken` in `PlannerApiFactory`'s in-memory configuration; verify the suite exercises real id derivation while the capture path stays inert

## 6. Contract coordination

- [x] 6.1 Verify the regenerated `artifacts/openapi` artifact contains `analyticsId` on the current-user response and is committed with this change
- [x] 6.2 Confirm the companion `tacticus-planner-apps` change `add-posthog-analytics` consumes `analyticsId` as specified, and that this change merges first

## 7. Repository gates

- [x] 7.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` and verify it reports no changes
- [x] 7.2 Run `dotnet build TacticusPlanner.slnx -c Release --no-restore` and verify it succeeds
- [x] 7.3 Run `dotnet test TacticusPlanner.slnx -c Release --no-build` and verify all tests pass
- [x] 7.4 Run `docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .` and verify the image builds

## 8. Staging/production rollout

Code and infra-as-code are done from this session; the two remaining steps are
Azure/PostHog actions the user runs interactively themselves (never scripted
by an agent — see `docs/secret-management.md`), and an actual deployment.

- [x] 8.1 Create the PostHog project on **US Cloud** and record its project token for staging and production configuration — one shared project token (`phc_yyw5...`) committed non-secret in `appsettings.Development.json` (local) and `tacticus-planner-infra`'s `params/stg.workload.bicepparam`/`params/prod.workload.bicepparam` (staging/production), all pointing at `https://us.i.posthog.com`, per explicit user decision to share one PostHog project across environments
- [x] 8.2 Wire `Analytics:IdentityKey`/`Analytics:ProjectToken`/`Analytics:HostUrl` through `tacticus-planner-infra` (`modules/container-app.bicep`, `env.bicep`, both workload param files) so the Container App resolves them the same way as `UserJot__ProjectSecret`; validated against live `rg-tp-stg` via `pnpm infra -- deploy workload --env stg --action validate` (no mutation). Creating the actual `analytics-identity-key` Key Vault secret is still the user's own interactive action:
  ```shell
  az keyvault secret set --vault-name kv-tp-stg --name analytics-identity-key --value <32-byte base64 value>
  ```
  (generate the value with `openssl rand -base64 32` immediately before running this — never write it to a file or script first; production has no provisioned resources yet, so its secret is created when production is provisioned)
- [x] 8.3 Land the companion `tacticus-planner-infra` PR wiring `Analytics__IdentityKey` into `modules/container-app.bicep` and documenting the secret as **never rotate** in `docs/secret-management.md` — done (see 8.2); still needs the user to open/merge the actual PR
- [ ] 8.4 After deploying to staging, verify `account_registered` and `guild_registered` arrive in PostHog attributed to the same analytics id the client reports — blocked on the user creating the Key Vault secret (8.2) and running an actual staging deploy (`pnpm infra -- deploy workload --env stg --action apply --image <immutable-digest>`), which this session does not do unprompted for a live shared environment
