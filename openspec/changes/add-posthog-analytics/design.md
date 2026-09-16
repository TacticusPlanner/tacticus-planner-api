## Context

See `proposal.md` — Why. Requirements are in `specs/analytics-identity/spec.md` and `specs/product-analytics-events/spec.md`. The companion frontend change is `add-posthog-analytics` in `tacticus-planner-apps`; this half applies first because it introduces the `AnalyticsId` field the client consumes.

Constraints that shape the approach:

- **The API already has an observability stack.** `ServiceDefaults/Extensions.cs` wires OpenTelemetry and Azure Monitor. That is operational telemetry — latency, dependencies, exceptions — and is not what this change touches. Product analytics is a separate concern with a separate destination and a separate privacy floor.
- **Tests must not reach the network.** `TacticusPlanner.Api.Tests` runs against EF Core InMemory with `ITacticusApi` and `ITacticusV1Client` swapped for fakes. Anything added here has to fit that.
- **The OpenAPI-generation host boots the real `Program`.** `Program.cs` detects it via `isOpenApiDocumentGeneration` and skips things that need real configuration. Any options validation added here has to account for that host.
- **`Features/UserJot` is the closest precedent** for an external-vendor integration: an options class bound to a config section, validated on start, with the secret arriving from Key Vault via `container-app.bicep`.

**No EF Core migration is required.** This change adds no entity, no property, and no schema change; the analytics id is derived on demand from the existing account identifier and is never persisted. There is no backfill and no data migration.

**No game catalog data changes.** No raw dataset, denormalization step, served projection, or manifest hash is affected, so the catalog manifest snapshot test is unaffected.

## Goals / Non-Goals

**Goals:**

- One boundary between the rest of the API and the analytics vendor, so call sites never reference the vendor SDK.
- An identity derivation that is cheap, deterministic, and impossible to reverse or recompute without the server-held key.
- Capture that is provably inert in tests and in any environment without analytics configuration.
- Event emission that cannot change the outcome or latency of the request that triggered it.

**Non-Goals:**

- Feature flags, experiments, or group analytics. Those are the other reason to adopt the vendor SDK and are deliberately not in this change; notably, none of them are needed here, which is why no personal API key is introduced.
- Replacing or duplicating anything in the OpenTelemetry/Application Insights pipeline. Exceptions and traces stay there.
- Any change to UserJot's identity, and any propagation of account purge to an external vendor.

## Decisions

### An internal capture abstraction, not the vendor client at call sites

Feature slices depend on a small internal interface (an `IProductAnalytics` with one method per declared event), implemented once over the vendor SDK. Call sites in `CurrentUser` and `Guilds` never see the vendor type.

*Why:* it gives one place to implement the inert mode, one place to enforce the property floor, and one seam for the test fake. Three requirements collapse into one implementation.

*Alternative considered — inject the vendor's client directly.* Rejected: every call site becomes vendor-coupled, the unconfigured case has to be handled repeatedly, and the privacy floor in `product-analytics-events` becomes a convention rather than something the type system holds.

### Typed event methods, not a free-form property bag

The abstraction exposes `AccountRegistered(...)` and `GuildRegistered(...)` rather than `Capture(string name, IDictionary<string, object>)`.

*Why:* the spec requires that events never carry display names, guild names, API keys, or free text. A property bag makes that a review rule that decays; typed methods make it a compile-time property. It also keeps the event catalog readable from the interface alone.

*Trade-off:* adding an event is a code change in the abstraction rather than a one-liner at the call site. That is the intent — `tacticus-planner-docs/analytics/events-catalog.md` requires documenting a purpose before adding an event, and this makes adding one a deliberate act.

### Identity: keyed HMAC with a per-destination label

`lowercase_hex(HMAC-SHA256(key, "<destination>:" + accountId))`, with `"posthog"` as the only destination in this change.

*Why:* it satisfies all four identity requirements at once — deterministic (stable id), keyed (not computable by someone who knows the account id), fixed-width output with no structure carried through (no creation time, no ordering), and label-separated (two destinations cannot cross-join).

*Alternative considered — unkeyed SHA-256.* Rejected. The account id is already client-visible via `/me`, so an unkeyed digest is computable by anyone who has seen one, and every destination would receive the identical value — failing the per-destination requirement.

*Alternative considered — a random id persisted per account.* Rejected: it needs a schema change and a migration, and it must then be deleted or retained explicitly, which reintroduces exactly the lifecycle question the pseudonymous derivation avoids.

### A dedicated, deliberately unversioned key

`Analytics:IdentityKey` is its own configuration value backed by its own Key Vault secret, and — unlike `ColumnEncryption` — it has no version map and no current-version pointer.

*Why not reuse `IColumnHashService`:* it derives its key from `ColumnEncryption:Keys:{CurrentKeyVersion}`. That scheme exists precisely so keys *can* be added and rolled forward. Rolling it forward would silently re-derive every analytics id, turning the entire user base into new people in the destination with no error and no obvious symptom. Analytics identity needs the opposite property from column encryption: it must never change.

*Why no versioning of its own:* versioning is the mechanism for rotation, and rotation is the failure mode. Omitting it makes the constraint structural rather than a documented warning. `secret-management.md` will state it as never-rotate.

### The identity key is required; the project token is optional

Options validation splits: `Analytics:IdentityKey` is validated on start (skipped for the OpenAPI-generation host, as `AddUserJotFeature` already does), because `AnalyticsId` is part of the `/me` contract and must always be derivable. `Analytics:ProjectToken` is *not* required — its absence is the supported way to run with capture switched off.

*Why:* these two settings have genuinely different obligations. Conflating them would either make analytics mandatory in every environment (breaking the inert-in-tests requirement) or make `/me` able to return a response without its `AnalyticsId` field.

Consequence: `PlannerApiFactory` supplies a fixed test `Analytics:IdentityKey` alongside the `ColumnEncryption` keys it already injects, and supplies no project token — so the test suite exercises the real derivation while the capture path stays inert by construction, not by mocking.

### Emission points

- `account_registered` is emitted from `GetCurrentUserEndpoint.ProvisionAccountAsync`, the single place an `Account` row is created. "At most once per account" therefore follows from the code path rather than needing a guard.
- `guild_registered` requires distinguishing a new registration from a re-registration, which `RegisterGuildEndpoint` does not currently know — `GuildSyncService.SynchronizeAsync` creates the `Guild` when absent and returns `GuildSyncResult.Success` either way. `Success` gains a flag reporting whether the guild was newly created, and the endpoint emits only when it is set.

*Alternative considered — re-query after the call to see whether the guild existed.* Rejected: a second query that can race with the write it is checking, to recover information the write already had.

### Fire-and-forget capture, no per-request flush

Calls are not awaited on the request path. The SDK queues and batches internally; the abstraction's methods return without waiting for delivery. No `FlushAsync` is added per request.

*Why:* the spec requires that reporting neither changes an outcome nor delays a response. Per-request flushing is the documented pattern for serverless hosts that may be frozen between invocations; this API is a long-running Container App, where it would add a network round trip to user-facing requests for no benefit.

*Consequence:* events buffered at shutdown can be lost. Accepted — see Risks.

## Risks / Trade-offs

- **Buffered events lost on shutdown or crash** → Accepted for the initial event set. `account_registered` and `guild_registered` are low-volume and, being lifecycle events, a rare loss does not distort a trend. Registering the client so the host disposes it during graceful shutdown recovers the ordinary redeploy case; it does not cover a hard crash.
- **The identity key is a single point of permanence.** Losing it makes every historical analytics id unreproducible; rotating it silently forks the user base → Key Vault is the system of record, the secret is documented as never-rotate, and the absence of a version map means there is no supported mechanism to rotate it by accident.
- **The destination label is part of the derivation, so changing the string `"posthog"` re-keys everyone** → It is a constant in the derivation, not configuration, so it cannot drift per environment.
- **Environments could diverge on region.** The client and server must both target US Cloud; a mismatch splits one person into two profiles across two clouds with no error → the host URL is set in configuration on both sides and verified during deployment rather than defaulted per-environment.
- **Typed event methods make ad-hoc instrumentation deliberately awkward** → Intended. The cost is paid when adding an event, which is when the events-catalog discipline is supposed to apply.

## Migration Plan

1. Create the `analytics-identity-key` Key Vault secret in each environment (`tacticus-planner-infra`, set interactively — it is not generated by repo tooling), and wire `Analytics__IdentityKey` into `container-app.bicep` alongside the existing `UserJot__ProjectSecret`.
2. Add the non-secret `Analytics:ProjectToken` and `Analytics:HostUrl` (`https://us.i.posthog.com`) to committed configuration.
3. Deploy this change. `AnalyticsId` appears on `/me` and the two events begin reporting.
4. Apply the companion `tacticus-planner-apps` change, which consumes `AnalyticsId`.

**Rollback:** remove `Analytics:ProjectToken` from configuration and restart. Capture stops; `/me` continues to return `AnalyticsId`, so the deployed client is unaffected. A full code rollback is also safe — the field is additive, so roll the apps change back first if both are being reverted.

## Open Questions

- Whether the buffered-event loss window justifies a shutdown flush hook beyond ordinary disposal. Answerable from observed volume after the first deployment; it changes neither the specs nor the task breakdown.
