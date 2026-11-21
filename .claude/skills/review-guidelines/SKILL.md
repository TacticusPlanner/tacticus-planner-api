---
name: review-guidelines
description: This repo's code review rules — what to flag in a diff or PR to the API, persistence, game catalog or tests, at what severity, and what to skip, pointing to the file or skill that owns each rule (AGENTS.md, README "Breaking contracts", game-catalog-data). Use when reviewing a diff or pull request in this repo, including inside the built-in /code-review or /simplify, or when triaging CodeRabbit findings.
---

# Review guidelines

These rules add to a general review; they don't replace it. Correctness bugs
come first. Use the conventions below to find what a general reviewer can't
know about this repo. Each rule names the file or skill that owns it. Open
that file to check a finding; don't review from memory.

Review the diff against `main` (the PR's base). Flag a convention only on
lines the diff adds or changes.

## Severity

- **Blocking**: a contract, column or config key the deployed client or
  existing data won't survive; an edited applied migration; a leaked secret or
  a missing auth check; a weakened test.
- **Should fix**: a broken rule from the sections below that the build,
  analyzers or tests don't already catch.
- **Nit**: naming or wording where the owning file allows either choice. Leave
  nits out when the review is capped.

When you report a convention finding, name the rule (file and section), for
example "AGENTS.md Database Migrations: range check belongs in a validator".

## Skip

- Formatting, using order and file-scoped namespaces. CI catches them with
  `dotnet format --verify-no-changes` and build warnings.
- The generated paths that `.coderabbit.yaml` filters out
  (`artifacts/openapi/**`, raw datamine files, migration `Designer.cs` and
  model snapshot, `packages.lock.json`). Review only that the change that
  produces them is in the same PR.

## 1. Contract compatibility (blocking)

V2 is in production, and the apps client deploys apart from the API. See
README "Breaking contracts" and AGENTS.md "Commit & Pull Request Guidelines".

- Flag a removed or renamed endpoint, field, enum value or query parameter,
  a field that became required, and a changed status code or error shape,
  unless the PR keeps the old form working. The order is: add, release the
  client, remove later.
- A changed contract shows up in the regenerated `artifacts/openapi`
  document. If the client uses it, the PR names the companion
  `tacticus-planner-apps` change.
- Config keys: a renamed or removed key needs the deployment configuration
  changed with it, or a fallback.

## 2. Persistence and migrations

See AGENTS.md "Database Migrations".

- Blocking: an edit to, or removal of, a migration that has run in production.
  Add a new migration instead.
- A model change comes with its migration in the same PR, and the migration
  matches the model change.
- The migration is safe on existing rows: a new non-null column has a default
  or a backfill, and nothing drops data users still need.
- Database constraints are keys, foreign keys, uniqueness and nullability
  only. Range and enum rules go in validators.
- Nothing under `Migrations/` is edited by hand.

## 3. Security and auth

See AGENTS.md "Configuration & Security".

- Blocking: a committed secret. The only exception is the throwaway dev key
  in `appsettings.Development.json`.
- Tacticus API keys, Tacticus user ids and V1 credentials never reach the
  client in full. They go through `SecretMasker`. V1 usernames and passwords
  are used once in the request and never stored or logged.
- A new endpoint is covered by the default authorization policy. Under
  `/api/v1` it needs the `access_as_user` scope. Only `/health/*` and the
  OpenAPI and `/docs` endpoints are anonymous.
- A query that reads or writes player data is scoped to the signed-in user.

## 4. Game catalog

See the `game-catalog-data` skill and `openspec/config.yaml`.

- Served datasets carry stable ids and structured values only. Flag a display
  name, icon path or other display string sent by the server.
- Raw datasets stay as the datamine pull wrote them. Derived values come from
  denormalization, not edits to raw files.
- A changed manifest or served shape updates the catalog manifest snapshot
  and adds `TacticusPlanner.GameCatalog.Tests` coverage.
- A datamine bump moves `GameCatalog/Data/datamine/REF` through `scripts/pull-datamine.cs`,
  on the same tag the apps repo uses.

## 5. Tests

See AGENTS.md "Testing Guidelines".

- New behavior has tests in the right project. Pure logic goes in
  `Api.Tests` or `GameCatalog.Tests`. HTTP and database behavior goes in
  `Api.IntegrationTests`. Migrations and constraints go in
  `Persistence.IntegrationTests`.
- Tests never call live upstream services or analytics.
- Test persistence matches production. Flag an alternate provider, a bypassed
  transaction or lock, or a constraint weakened to make seeding easier.
- Blocking: a weakened or removed assertion, a skipped test, an edited
  snapshot with no behavior change, or a test that covers only the happy path
  of new branching logic.

## 6. CI, build and dependencies

- Package versions live in `Directory.Packages.props`. Lock files change with
  them (the AppHost has none on purpose).
- Workflows: flag a removed or skipped check, a lowered coverage threshold,
  and a new permission or secret reaching a job that runs PR code.
- A new dependency needs a reason, and nothing in the solution already does
  the job.
