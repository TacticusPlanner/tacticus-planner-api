## Why

V2 ships no product analytics at all. The only usage evidence the project has is GA4 on the V1 app, which cannot answer whether the rewrite is working: it does not know V2 exists, and it cannot see server-truth outcomes at all. Meanwhile `tacticus-planner-docs/analytics/` already defines the privacy posture and event-taxonomy discipline this should follow, and has been waiting on a provider.

PostHog is that provider. This change is the backend half; the companion `tacticus-planner-apps` change `add-posthog-analytics` is the frontend half and applies second.

The backend half exists for two reasons the client cannot cover:

- **Server-truth events.** Account provisioning and guild registration complete inside the API. The browser can only observe that it made a request, not what the server actually did.
- **Ad-blocker immunity.** `*.posthog.com` is widely blocked, and this product's audience skews toward blockers. Client-side capture silently under-reports; server-side capture always lands. The events that decide product questions belong here by preference, not as a fallback.

## What Changes

- Add the official `PostHog.AspNetCore` SDK and a new `Features/Analytics` slice, registered from `Program.cs` alongside the other feature slices.
- Capture two server-side events, both keyed to the caller's pseudonymous analytics id:
  - `account_registered` — emitted from first-access provisioning in `GetCurrentUserEndpoint.ProvisionAccountAsync`, the true signup moment.
  - `guild_registered` — emitted when a guild is first registered for a profile.
- Derive a **pseudonymous analytics id** instead of sending the raw `AccountId` to PostHog: `HMAC-SHA256(secret, "posthog:" + accountId)`, rendered as lowercase hex. Expose it to the browser as a new `AnalyticsId` field on `CurrentUserResponse` so the client identifies the same person under the same id.
- Configure the PostHog project token and host URL (`https://us.i.posthog.com`, US Cloud) as ordinary non-secret configuration. Add one new Key Vault secret for the analytics identity HMAC key.

### Decisions already taken

| Decision | Choice | Rationale |
|---|---|---|
| Region | **US Cloud** (`https://us.i.posthog.com`) | Chosen by the maintainer. Must match the client exactly; PostHog has no self-serve migration between clouds. |
| Who is tracked | **Identified users only** | No anonymous capture anywhere, client or server. Every event is tied to an account that already has a relationship with the product, which removes the anonymous→identified merge problem and the need for a consent banner covering anonymous visitors. |
| Identity sent to PostHog | **Pseudonymous HMAC, not the raw id** | `AccountId` is a v7 GUID (`GetCurrentUserEndpoint.cs:82`), whose first 48 bits are the creation timestamp in plaintext (RFC 9562). Sending it raw discloses every account's signup time and, because v7 sorts, the signup ordering of the whole user base. It is also already client-visible via `/me`, so it would act as a join key between PostHog, the browser, and any other vendor. |
| HMAC key | **A dedicated secret, not the column-encryption key** | `HmacColumnHashService` keys off `ColumnEncryption:Keys:{CurrentKeyVersion}`. Reusing it would mean that introducing `column-encryption-key-v2` and bumping `CurrentKeyVersion` silently re-keys every analytics identity, turning the entire user base into new people in PostHog. The analytics key must be independently immutable. |
| Account purge | **Does not propagate to PostHog** | `DELETE /me` deletes planner data and leaves PostHog untouched. PostHog never receives a name, an email, or any other identifying property — only an opaque HMAC and event names — and purging the account destroys the only mapping back to a person, so what remains carries no route to re-identification. This is what makes the pseudonymous id load-bearing rather than cosmetic: it is the reason the retained data is defensible. |
| Initial event set | `account_registered`, `guild_registered` | Deliberately small. Both are unambiguous server-truth moments on existing code paths; no new seams. Further events (V1 import outcomes, player-data sync, Tacticus integration activation) are follow-on work once the pipeline is proven. |

## Capabilities

### New Capabilities

- `analytics-identity`: derivation of the stable pseudonymous analytics id from an account, and its exposure to the client.
- `product-analytics-events`: the server-side captured product events — which moments emit, what properties they carry, and the privacy floor those properties must respect.

### Modified Capabilities

(none — no existing API capability's requirements change. `userjot-identity-token` is deliberately untouched; see Impact.)

## Impact

**`tacticus-planner-api`**

- New `src/TacticusPlanner.Api/Features/Analytics/` slice: options, the identity deriver, and a thin capture abstraction. New `AddAnalyticsFeature(...)` call in `Program.cs`, following `AddUserJotFeature`'s `validateOnStart` handling for the OpenAPI-generation host.
- `Features/CurrentUser/GetCurrentUserEndpoint.cs`: new `AnalyticsId` field on `CurrentUserResponse` (additive), plus the `account_registered` emit in `ProvisionAccountAsync`.
- `Features/Guilds/`: the `guild_registered` emit.
- `Features/AccountManagement/PurgeAccountEndpoint.cs`: **unchanged.** Account purge does not notify PostHog.
- **OpenAPI artifact regenerates** (`CurrentUserResponse` gains a field) and must be coordinated with the companion apps change.
- New NuGet dependency `PostHog.AspNetCore`, centrally versioned in `Directory.Packages.props`; lock files refresh.
- `TacticusPlanner.Api.Tests` must stay free of outbound network calls. `PlannerApiFactory` swaps the PostHog client for a recording fake, the same way it already swaps `ITacticusApi` and `ITacticusV1Client` — which additionally makes "did this endpoint emit the right event with the right properties" an ordinary assertion.

**`tacticus-planner-infra`** (no OpenSpec store; tracked as a normal PR)

- New Key Vault secret `analytics-identity-key`, injected as `Analytics__IdentityKey` via `modules/container-app.bicep`, following the `userjot-project-secret` pattern.
- `docs/secret-management.md` documents it as **never rotate** — rotating it re-keys every analytics identity and permanently orphans all prior PostHog data.
- The PostHog project token and host URL are public and need no Key Vault entry.

**Out of scope**

UserJot is untouched by this change. Its `sub` continues to carry the raw `AccountId`, and account purge continues not to notify it. Re-keying UserJot onto the same pseudonymous identity (`"userjot:" + accountId`) would extend the reasoning above to that vendor too, but it re-keys every existing UserJot user and orphans their feedback, so it needs its own change and its own decision now that UserJot is live.
