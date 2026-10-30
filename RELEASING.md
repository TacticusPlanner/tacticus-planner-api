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

Production only ships published, immutable `vX.Y.Z` tags. Three workflows are
involved:

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `CD Stage` | every push to `main` | publishes the API image and deploys it to staging |
| `Release` | by hand (Actions tab) | cuts or patches a release, tags it, publishes the GitHub Release, then calls `CD Prod` |
| `CD Prod` | called by `Release`, or by hand | deploys an existing published release tag to production |

### One-time setup

- **Approval:** Settings → Environments → `production` → add required
  reviewers. Every production deploy then waits for one of them.
- **Deployment branches:** limit `production` to `main` and `release/**`.
- **Staging environment:** if `stage` is branch-restricted, also allow
  `release/**`, because image publishing runs in that environment.
- **Rulesets:** let `github-actions[bot]` create `release/**` branches and
  `v*` tags, otherwise the Release workflow cannot push them.
- **Variables:** the `production` environment needs the variables (not
  secrets) `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
  `ACR_NAME`, `CONTAINER_APP_NAME` and `RESOURCE_GROUP_NAME`. The infra
  repository's `access pre --env prod` step sets all six.

### What a Release run does

1. **Tag:** computes the version, pushes `release/X.Y` (minor/major only) and
   an annotated `vX.Y.Z` tag in one atomic push, then publishes a GitHub
   Release with notes generated since the previous tag.
2. **Check:** confirms the tag is a published, non-draft release.
3. **Build:** reuses the image staging already published for that commit (`tacticus-planner-api:<sha>`), or builds and pushes it if none exists. Production always deploys the `@sha256:` digest.
4. **Approve:** the deploy job waits for a `production` reviewer.
5. **Deploy:** updates the production Container App to that digest and waits for a healthy, running revision.

The tag and the GitHub Release exist before approval. Rejecting leaves the
release published but not live; ship it later with `CD Prod` (see below).

### Versions

| Bump | Run from | Result |
| --- | --- | --- |
| `major` | `main` | `v(X+1).0.0` and branch `release/(X+1).0` |
| `minor` | `main` | `vX.(Y+1).0` and branch `release/X.(Y+1)` |
| `patch` | `release/X.Y` | next `vX.Y.Z` on that branch, no new branch |

With no tags yet, `major` gives `v1.0.0` and `minor` gives `v0.1.0`. Use
`major` for the first production launch.

## Example: cutting a release

The latest release is `v1.3.2` and `main` has new, staging-verified work.

1. Actions → **Release** → **Run workflow**.
2. *Use workflow from:* `main`; *bump:* `minor`; leave *commit* empty.
3. The run creates `release/1.4` from the head of `main`, tags `v1.4.0`, and
   publishes the release with notes since `v1.3.2`.
4. Open the run, click **Review deployments**, tick `production`, then
   **Approve and deploy**.
5. `v1.4.0` is live.

**Shipping an older commit.** If the head of `main` is not ready but
`9f8e7d6` is, run the same thing with *commit* `9f8e7d6`. The commit must be on
`main`, come after the last `vX.Y.0` cut (here `v1.3.0`), and not already be
released. It must also contain `release.yml` and `scripts/release/`, because
later patches run from the release branch cut at that commit.

## Example: patching a release

`v1.4.0` is live and has a bug.

1. Fix it on `main` first through a normal pull request, so the next release
   keeps the fix. Say the squash commit is `5a6b7c8`.
2. Backport it to the release branch:

   ```bash
   git fetch origin
   git switch -c backport/fix-login origin/release/1.4
   git cherry-pick 5a6b7c8
   git push -u origin backport/fix-login
   ```

3. Open a pull request from `backport/fix-login` into `release/1.4`, get it
   reviewed, and squash-merge it.
4. Actions → **Release** → *Use workflow from:* `release/1.4`; *bump:*
   `patch`.
5. The run tags `v1.4.1` with notes since `v1.4.0`. Approve the deploy.

A second fix on the same line becomes `v1.4.2`. Anything merged to `main`
after `release/1.4` was cut stays out of the patch.

A patch commit exists only on the release branch, so its image is built fresh and
never ran on staging. Confirm the fix on staging from `main` before releasing
the patch.

## Redeploying, rolling back and failed runs

- **Ship a rejected or failed deploy:** Actions → **CD Prod** → *Use workflow
  from:* `main`; *ref:* `v1.4.1`. It refuses anything that is not a published,
  non-draft `vX.Y.Z` release.
- **Roll back:** run **CD Prod** with the previous release tag, for example
  `v1.4.0`. Check the migration notes below first: rolling back past a destructive migration needs a database restore.
- **Release run failed after the tag was pushed, before the GitHub Release
  was published:** use **Re-run all jobs** on that run. It finds the tag
  without a GitHub Release and resumes publishing and deploying instead of
  cutting a new version.
- **Failed after the GitHub Release was published:** a rerun refuses the
  already released commit; ship the tag with **CD Prod** instead.
- **Failed before the tag was pushed:** nothing was created; fix the cause and
  run Release again.

## Release scripts

The version logic (bumps, patches, older commits, resuming a failed run) lives in
`scripts/release/` as plain Node modules with no dependencies:

- `plan-release.mjs`: the rules, as a pure function.
- `git-repo.mjs`: answers its questions with git and the GitHub CLI.
- `compute-version.mjs`: the entry point the Release workflow runs.

Run the tests on any OS with Node 22 or newer:

```bash
node --test "scripts/release/*.test.mjs"
```

CI runs them on Linux and Windows. The folder is kept identical in
`tacticus-planner-apps`; change both together.

## Rules

- Never move, delete or reuse a published tag.
- Never merge features into a release branch; only backports of fixes already
  on `main`.
- Keep `release/X.Y` only while that release line is supported.

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
