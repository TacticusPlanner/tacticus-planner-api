// Decides what a Release workflow run ships. Pure: every fact about the repository comes from
// the `repo` argument, so the rules are unit-tested without git (see plan-release.test.mjs).
// Kept identical in tacticus-planner-apps and tacticus-planner-api.

const VERSION_TAG = /^v(\d+)\.(\d+)\.(\d+)$/
const RELEASE_BRANCH = /^release\/(\d+)\.(\d+)$/

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

/** Returns the highest vX.Y.Z tag whose version matches `filter`, or "" when none does. */
export function latestTag(tags, filter = () => true) {
  let best = ""
  let bestVersion = null
  for (const tag of tags) {
    const version = parseVersion(tag)
    if (
      version &&
      filter(version) &&
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
 * @param {{ bump: "major" | "minor" | "patch", refName: string, commit?: string }} inputs
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
  if (!["major", "minor", "patch"].includes(bump)) {
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

  return bump === "patch"
    ? planPatch(refName, tags, sha)
    : planCut(bump, refName, tags, sha, repo)
}

// An older commit on main can be released instead of its head; patches always ship the head of
// their release branch.
function resolveTarget({ bump, refName, commit }, repo, tags) {
  if (!commit) {
    return repo.head()
  }
  if (bump === "patch") {
    throw new ReleaseError(
      "commit applies to minor/major releases only; patch ships the head of the release branch."
    )
  }
  const sha = repo.resolveCommit(commit)
  if (!sha) {
    throw new ReleaseError(`${commit} is not a commit in this repository.`)
  }
  if (!repo.isAncestor(sha, repo.head())) {
    throw new ReleaseError(`${commit} is not on ${refName}.`)
  }
  // Every vX.Y.0 is cut from main, so the newest one marks what main has already shipped.
  const lastCut = latestTag(tags, (v) => v.patch === 0)
  if (lastCut && !repo.isAncestor(lastCut, sha)) {
    throw new ReleaseError(
      `${commit} predates ${lastCut}; pick a commit after the last release.`
    )
  }
  return sha
}

function planPatch(refName, tags, sha) {
  const match = RELEASE_BRANCH.exec(refName)
  if (!match) {
    throw new ReleaseError("Run patch releases from a release/X.Y branch.")
  }
  const [major, minor] = match.slice(1).map(Number)
  const previous = latestTag(
    tags,
    (v) => v.major === major && v.minor === minor
  )
  if (!previous) {
    throw new ReleaseError(
      `release/${major}.${minor} has no v${major}.${minor}.* tag to patch.`
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
  const last = previous
    ? parseVersion(previous)
    : { major: 0, minor: 0, patch: 0 }
  const major = bump === "major" ? last.major + 1 : last.major
  const minor = bump === "major" ? 0 : last.minor + 1
  const branch = `release/${major}.${minor}`
  if (repo.remoteBranchExists(branch)) {
    throw new ReleaseError(`${branch} already exists.`)
  }
  return { tag: `v${major}.${minor}.0`, previous, branch, sha, resume: false }
}
