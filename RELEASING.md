# Releasing Tacticus Planner API

This repository uses trunk-based development with just-in-time release
branches. `main` is the integration branch and must remain releasable.

## Development flow

1. Create a short-lived topic branch from `main`.
2. Open a pull request into `main`.
3. Enable squash auto-merge when the pull request is ready.
4. Obtain the required approval and pass all required checks.
5. GitHub squash-merges the pull request and deletes the topic branch after
   every ruleset requirement is satisfied.

Auto-merge does not bypass reviews or checks. New commits dismiss stale
approvals and must pass the required checks again.

Incomplete functionality merged into `main` must be disabled with a feature
flag. A push to `main` publishes and deploys the current API to staging.

## Release flow

Releases are cut and shipped by the `Release` workflow (Actions tab, run by
hand). Each run creates an immutable annotated `vX.Y.Z` tag, publishes a
GitHub Release from it with generated notes, and deploys the commit's immutable container digest (the one staging ran when the commit is on `main`) to production.
Production never deploys from a moving branch.

For a new minor or major release:

1. Check that the head of `main` has passed CI and staging validation.
2. Run `Release` from `main` with `minor` (or `major`). It creates
   `release/X.Y` from `main`, tags `vX.Y.0`, publishes the release, and
   deploys it.

Keep `release/X.Y` only while that release line is supported. Do not merge
features into a release branch.

## Patch flow

Reproduce and fix production bugs on `main` first:

1. Merge the fix into `main` through the normal pull-request flow.
2. Create a backport branch from the supported `release/X.Y` branch.
3. Cherry-pick the squash commit from `main`.
4. Open a pull request from the backport branch into `release/X.Y`.
5. After approval and required checks, squash-merge the backport.
6. Run `Release` from `release/X.Y` with `patch`. It tags the next patch
   version, such as `v1.4.1`, publishes the release with notes since the
   previous `vX.Y.*`, and deploys it.

`CD Prod` redeploys an existing release tag by hand; it refuses anything that
is not a published `vX.Y.Z` release.

Never move or reuse a published tag.

## Compatibility and migrations

Deploy compatible API changes before the apps release that consumes them.
API contracts and database migrations must use an expand-and-contract rollout
when old and new application versions may overlap. Destructive cleanup belongs
in a later release after all deployed consumers have moved to the replacement.

The application applies pending EF Core migrations during startup. Validate
migrations against a production-like database before promoting the image and
confirm that rollback does not require reversing an already-applied destructive
migration.

### Data migrations that drop or rewrite data

Some migrations are deliberately not reversible in place, for example
`AddGlobalGoalPriority`, which folds every profile's per-project goal orders
into one account-wide order and drops `project_goals.priority`. For these:

1. Take a verified database backup immediately before deploying the release
   (`pg_dump --format=custom`, then confirm the dump restores into a scratch
   database and that row counts match).
2. Deploy the API first; the migration runs at startup inside one transaction
   and aborts, leaving the schema unchanged, if its built-in invariant check
   fails (every in-flight goal holds exactly one dense position per profile).
3. After startup, spot-check the counts: `count(*)` of Active/Paused goals per
   profile equals `count(global_priority)`.
4. Prefer forward repair if the cutover misbehaves. Rolling back to an old
   binary requires restoring the pre-migration backup, because the old
   per-project orders no longer exist; the migration's `Down` only
   reconstructs an approximation from the global order.

## Coordinated apps and API releases

Apps and API versions use Semantic Versioning independently. A production
release record must identify:

- the apps tag;
- the API tag;
- the immutable API container digest;
- the deployment timestamp.
