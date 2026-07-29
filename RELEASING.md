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

For a new minor or major release:

1. Select a commit on `main` that has passed CI and staging validation.
2. Create `release/X.Y` from that commit.
3. Validate the release candidate with its compatible apps release.
4. Apply stabilization fixes through the patch flow below.
5. Create an immutable annotated `vX.Y.Z` tag on the accepted commit.
6. Publish a GitHub Release from that tag.
7. Deploy production from the container digest built from the tagged commit,
   never from a moving branch or mutable image tag.

Keep `release/X.Y` only while that release line is supported. Do not merge
features into a release branch.

## Patch flow

Reproduce and fix production bugs on `main` first:

1. Merge the fix into `main` through the normal pull-request flow.
2. Create a backport branch from the supported `release/X.Y` branch.
3. Cherry-pick the squash commit from `main`.
4. Open a pull request from the backport branch into `release/X.Y`.
5. After approval and required checks, squash-merge the backport.
6. Tag the result with the next patch version, such as `v1.4.1`.
7. Publish the GitHub Release and deploy its immutable container digest.

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

## Coordinated apps and API releases

Apps and API versions use Semantic Versioning independently. A production
release record must identify:

- the apps tag;
- the API tag;
- the immutable API container digest;
- the deployment timestamp.
