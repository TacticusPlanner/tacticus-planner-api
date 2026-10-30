// The real `repo` for planRelease: answers its questions with git and the GitHub CLI.

import { spawnSync } from "node:child_process"

/**
 * @param {{ cwd?: string, releaseExists?: (tag: string) => boolean }} [options]
 *   `releaseExists` defaults to asking `gh release view`; tests pass their own.
 */
export function createGitRepo({
  cwd = process.cwd(),
  releaseExists = ghReleaseExists,
} = {}) {
  const git = (...args) => run("git", args, cwd)
  const lines = (text) => text.split("\n").filter(Boolean)

  return {
    head: () => git("rev-parse", "HEAD").stdout.trim(),
    resolveCommit(ref) {
      const result = git("rev-parse", "--verify", "--quiet", `${ref}^{commit}`)
      return result.status === 0 ? result.stdout.trim() : null
    },
    isAncestor(ancestor, descendant) {
      const result = git("merge-base", "--is-ancestor", ancestor, descendant)
      if (result.status > 1) {
        throw new Error(`git merge-base failed: ${result.stderr.trim()}`)
      }
      return result.status === 0
    },
    tags: () => lines(checked(git("tag", "--list")).stdout),
    tagsAt: (sha) => lines(checked(git("tag", "--points-at", sha)).stdout),
    remoteBranchExists(branch) {
      // Exit code 2 means no matching ref; anything else non-zero is a real failure.
      const result = git(
        "ls-remote",
        "--exit-code",
        "--heads",
        "origin",
        branch
      )
      if (result.status !== 0 && result.status !== 2) {
        throw new Error(`git ls-remote failed: ${result.stderr.trim()}`)
      }
      return result.status === 0
    },
    releaseExists,
  }
}

function ghReleaseExists(tag) {
  const result = run("gh", [
    "release",
    "view",
    tag,
    "--repo",
    process.env.GITHUB_REPOSITORY,
    "--json",
    "tagName",
  ])
  return result.status === 0
}

function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, encoding: "utf8" })
  if (result.error) {
    throw result.error
  }
  return result
}

function checked(result) {
  if (result.status !== 0) {
    throw new Error(result.stderr.trim())
  }
  return result
}
