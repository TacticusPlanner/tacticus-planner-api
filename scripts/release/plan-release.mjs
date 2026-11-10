// Decides what a Release workflow run ships. Pure: every fact about the repository comes from
// the `repo` argument, so the rules are unit-tested without git (see plan-release.test.mjs).
// Kept identical in tacticus-planner-apps and tacticus-planner-api.

const VERSION_TAG = /^v(\d+)\.(\d+)\.(\d+)$/
// release/X.Y comes from a minor or major cut, release/X.Y.Z from a patch cut.
const RELEASE_BRANCH = /^release\/(\d+)\.(\d+)(?:\.(\d+))?$/
const BUMPS = ["major", "minor", "patch", "hotfix"]

export class ReleaseError extends Error {
  name = "ReleaseError"
}

/** Parses a strict vX.Y.Z tag, or returns null for anything else. */
export function parseVersion(tag) {
  const match = VERSION_TAG.exec(tag)
  if (!match) {
    return null
  }
  const [major, minor, patch] = match.slice(1).map(Number)
  return { major, minor, patch }
}

export function compareVersions(a, b) {
  return a.major - b.major || a.minor - b.minor || a.patch - b.patch
}

/**
 * Returns the highest vX.Y.Z tag for which `filter(version, tag)` is true, or "" when none is.
 */
export function latestTag(tags, filter = () => true) {
  let best = ""
  let bestVersion = null
  for (const tag of tags) {
    const version = parseVersion(tag)
    if (
      version &&
      filter(version, tag) &&
      (!bestVersion || compareVersions(version, bestVersion) > 0)
    ) {
      best = tag
      bestVersion = version
    }
  }
  return best
}

/**
 * Plans a release.
 *
 * major, minor and patch run from main and cut a new release branch; hotfix runs from a release
 * branch and tags its head as the next patch on that line.
 *
 * @param {{ bump: "major" | "minor" | "patch" | "hotfix", refName: string, commit?: string }} inputs
 * @param {{
 *   head(): string,
 *   resolveCommit(ref: string): string | null,
 *   isAncestor(ancestor: string, descendant: string): boolean,
 *   tags(): string[],
 *   tagsAt(sha: string): string[],
 *   remoteBranchExists(branch: string): boolean,
 *   releaseExists(tag: string): boolean,
 * }} repo
 * @returns {{ tag: string, previous: string, branch: string, sha: string, resume: boolean }}
 */
export function planRelease({ bump, refName, commit = "" }, repo) {
  if (!BUMPS.includes(bump)) {
    throw new ReleaseError(`Unknown bump "${bump}".`)
  }
  const tags = repo.tags()
  const sha = resolveTarget({ bump, refName, commit }, repo, tags)

  // An earlier run pushed the tag but did not publish the release: finish that one instead of
  // cutting a new version. Its branch, if any, went up with the tag.
  const existing = repo.tagsAt(sha).find((tag) => parseVersion(tag))
  if (existing) {
    if (repo.releaseExists(existing)) {
      throw new ReleaseError(`${sha} is already released as ${existing}.`)
    }
    const version = parseVersion(existing)
    const previous = latestTag(tags, (v) => compareVersions(v, version) < 0)
    return { tag: existing, previous, branch: "", sha, resume: true }
  }

  return bump === "hotfix"
    ? planHotfix(refName, tags, sha, repo)
    : planCut(bump, refName, tags, sha, repo)
}

// An older commit on main can be released instead of its head; a release branch always ships
// its head.
function resolveTarget({ bump, refName, commit }, repo, tags) {
  if (!commit) {
    return repo.head()
  }
  if (bump === "hotfix") {
    throw new ReleaseError(
      "commit applies to releases from main only; a release branch ships its head."
    )
  }
  const sha = repo.resolveCommit(commit)
  if (!sha) {
    throw new ReleaseError(`${commit} is not a commit in this repository.`)
  }
  if (!repo.isAncestor(sha, repo.head())) {
    throw new ReleaseError(`${commit} is not on ${refName}.`)
  }
  // Every cut (major, minor or patch) tags a commit on main, so the newest tag in main's history
  // marks what main has already shipped. Hotfix tags live only on release branches.
  const lastCut = latestTag(tags, (_, tag) => repo.isAncestor(tag, repo.head()))
  if (lastCut && !repo.isAncestor(lastCut, sha)) {
    throw new ReleaseError(
      `${commit} predates ${lastCut}; pick a commit after the last release.`
    )
  }
  return sha
}

// A hotfix ships the head of a release branch as the next patch on its line. That branch must
// already contain the line's latest tag; otherwise the hotfix would silently drop whatever that
// release shipped (for example a patch cut from main onto its own release/X.Y.Z branch).
function planHotfix(refName, tags, sha, repo) {
  const match = RELEASE_BRANCH.exec(refName)
  if (!match) {
    throw new ReleaseError(
      "Run hotfix releases from a release/X.Y or release/X.Y.Z branch."
    )
  }
  const [major, minor] = match.slice(1, 3).map(Number)
  const previous = latestTag(
    tags,
    (v) => v.major === major && v.minor === minor
  )
  if (!previous) {
    throw new ReleaseError(
      `${refName} has no v${major}.${minor}.* tag to hotfix.`
    )
  }
  if (!repo.isAncestor(previous, sha)) {
    throw new ReleaseError(
      `${refName} does not contain ${previous}; hotfix the branch that release was cut on.`
    )
  }
  const patch = parseVersion(previous).patch + 1
  return {
    tag: `v${major}.${minor}.${patch}`,
    previous,
    branch: "",
    sha,
    resume: false,
  }
}

function planCut(bump, refName, tags, sha, repo) {
  if (refName !== "main") {
    throw new ReleaseError(`Run ${bump} releases from main.`)
  }
  const previous = latestTag(tags)
  if (bump === "patch" && !previous) {
    throw new ReleaseError(
      "Nothing to patch yet; run a minor or major release first."
    )
  }
  // With no tags yet, count from 1.0.0: this rewrite ships as V2 of Tacticus Planner.
  const last = previous
    ? parseVersion(previous)
    : { major: 1, minor: 0, patch: 0 }
  const { major, minor, patch } = nextVersion(bump, last)
  // A patch gets a branch of its own, release/X.Y.Z, so later hotfixes build on exactly what it
  // shipped instead of on an older release/X.Y that lacks it.
  const branch =
    bump === "patch"
      ? `release/${major}.${minor}.${patch}`
      : `release/${major}.${minor}`
  if (repo.remoteBranchExists(branch)) {
    throw new ReleaseError(`${branch} already exists.`)
  }
  return {
    tag: `v${major}.${minor}.${patch}`,
    previous,
    branch,
    sha,
    resume: false,
  }
}

function nextVersion(bump, last) {
  switch (bump) {
    case "major":
      return { major: last.major + 1, minor: 0, patch: 0 }
    case "minor":
      return { major: last.major, minor: last.minor + 1, patch: 0 }
    default:
      return { major: last.major, minor: last.minor, patch: last.patch + 1 }
  }
}
