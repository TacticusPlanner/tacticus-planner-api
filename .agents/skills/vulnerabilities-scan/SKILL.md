---
name: vulnerabilities-scan
description: Check this repo's NuGet dependency tree for known vulnerabilities via `dotnet list package --vulnerable`, and fix them by editing the centrally-managed version in Directory.Packages.props. Use when asked to check this repo for dependency vulnerabilities, clear NuGet advisories, or address this repo's GitHub security/dependabot page.
---

# Vulnerabilities scan (NuGet dependencies)

Scans this repo's NuGet dependency tree for known CVEs. Dry-run validated:
`dotnet list package --vulnerable --include-transitive` correctly reported
zero vulnerabilities across all 10 projects in `TacticusPlanner.slnx`,
confirming the scan command and the fix path below (no advisories existed to
actually fix at that time).

## Scan

```bash
dotnet restore TacticusPlanner.slnx
dotnet list TacticusPlanner.slnx package --vulnerable --include-transitive
```

Optionally cross-check against GitHub's own alert feed — it and the local
scanner can each know about advisories the other doesn't yet, so treat the
union as the real list:

```bash
gh api repos/TacticusPlanner/tacticus-planner-api/dependabot/alerts --paginate
```

## No "routine update" phase — NuGet pins exactly

This repo uses central package management
(`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`): every
package version is pinned exactly, there's no semver range to begin with.
Unlike the pnpm/npm repos in this workspace, there's no "update within an
already-allowed range" step to run first — the fix step *is* the direct
edit. Don't reach for a per-project override; `Directory.Packages.props` is
the single source of truth for versions.

## Process

1. **Scan first** (`dotnet list package --vulnerable --include-transitive`)
   to get the current, real list — don't assume a GitHub alert page is
   complete or current.
2. **For each flagged package**, find its `<PackageVersion Include="..."
   Version="..." />` entry in `Directory.Packages.props` (or the per-project
   `.csproj` if it isn't centrally managed) and bump `Version` to the
   advisory's patched version.
   - **The flagged package is a direct dependency**: bump its
     `PackageVersion` entry directly.
   - **The flagged package is transitive** (pulled in by another package,
     not referenced by any `.csproj` directly): first check whether bumping
     the *direct* parent package (the one that references it) also brings
     in a patched version of the transitive one — that's usually the
     better fix since it stays on a version combination NuGet actually
     tested together. Only add a standalone `PackageVersion` pin for the
     transitive package itself if the parent hasn't published a version
     that resolves it.
   - **Patch/minor bump within the same major**: apply directly.
   - **Fix requires a major-version bump**: do not apply silently. Flag it
     to the user with the advisory, the affected package, and what the
     major bump would touch — that's a judgment call about breaking
     changes (API shape, EF Core migrations, etc.), not a routine
     dependency fix.
3. **Verify nothing broke**, matching this repo's actual build commands
   (root `AGENTS.md` → Build, Test, and Development Commands — locked
   restore for the API/ServiceDefaults projects, AppHost restore is
   intentionally unlocked, `Release` config, `--no-restore`/`--no-build`
   downstream):
   ```powershell
   dotnet restore src/TacticusPlanner.Api --locked-mode
   dotnet restore orchestration/TacticusPlanner.AppHost
   dotnet list TacticusPlanner.slnx package --vulnerable --include-transitive
   dotnet build TacticusPlanner.slnx -c Release --no-restore
   dotnet test TacticusPlanner.slnx -c Release --no-build
   ```
   Re-running `--locked-mode` restore after a `Directory.Packages.props`
   edit also confirms the packages actually resolve as expected, not just
   that the version string changed.
4. **Commit on a topic branch, not `main`** (this repo's convention — see
   root `AGENTS.md`). Do not push unless the user explicitly asks; creating
   the branch and committing locally is the deliverable of this skill.
5. **Report back**: which advisories closed, before/after versions of
   touched packages, anything intentionally left open (major bumps) and
   why, and confirmation that restore/build/tests all passed.
