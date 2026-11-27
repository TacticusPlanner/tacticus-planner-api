## Why

When `DELETE account?deleteSignInAccount=true` cannot delete the Entra sign-in account (Graph down, throttled, or a token failure), the planner data is gone but the sign-in account stays behind, and nothing tries again. The API also has no way to run work outside a request, which every later background feature (batch aggregates, scheduled syncs) needs.

No companion apps change: the response contract is unchanged.

## What Changes

- Hangfire runs inside the API process with PostgreSQL storage in a `hangfire` schema of its own database, `tacticus_planner_hangfire`, on the planner server (`ConnectionStrings:hangfire-db`, falling back to the planner database when unset, as in the test hosts). Hangfire creates and upgrades the schema itself. One server per replica runs the `default` queue with 2 workers and its own Npgsql pool of 2. No dashboard, no recurring jobs.
- `BackgroundJobs:ServerEnabled` (default `true`) turns the server off; the test hosts and OpenAPI generation run without it.
- When the inline Entra deletion fails for a token with a valid `oid` and a configured Graph credential, the purge enqueues `ExternalIdentityDeletionJob`, which Hangfire retries 8 times with a growing delay and then leaves failed. The response still reports `X-Sign-In-Account-Deleted: false`.

## Capabilities

### Modified Capabilities

- `account-purge`: a sign-in deletion that fails during the request is retried in the background.

## Impact

- `src/TacticusPlanner.BackgroundJobs` (new project: Hangfire setup and options), `src/TacticusPlanner.Api` `Features/AccountManagement`, `Program.cs`, `Directory.Packages.props` (Hangfire.Core, Hangfire.NetCore, Hangfire.PostgreSql; Newtonsoft.Json pinned to 13.0.4).
- Database: a new `tacticus_planner_hangfire` database (created by the infra and by Aspire) holding a `hangfire` schema managed by Hangfire, not by EF migrations.
- Infra: tacticus-planner-infra caps prod at 2 replicas so (11 API + 2 Hangfire) x 2 + 2 jobs = 28 of the 35 usable connections. It must be applied before this deploys.
