// Runs planRelease against real git repositories in a temp folder, with a bare clone as origin.

import assert from "node:assert/strict"
import { spawnSync } from "node:child_process"
import { mkdtempSync, rmSync } from "node:fs"
import { tmpdir } from "node:os"
import path from "node:path"
import { after, before, describe, it } from "node:test"

import { createGitRepo } from "./git-repo.mjs"
import { planRelease } from "./plan-release.mjs"

function git(cwd, ...args) {
  const result = spawnSync(
    "git",
    [
      "-c",
      "user.name=test",
      "-c",
      "user.email=test@example.com",
      "-c",
      "commit.gpgsign=false",
      "-c",
      "tag.gpgsign=false",
      ...args,
    ],
    { cwd, encoding: "utf8" }
  )
  assert.equal(result.status, 0, result.stderr)
  return result.stdout.trim()
}

describe("createGitRepo", () => {
  let root
  let work
  const commits = []

  before(() => {
    root = mkdtempSync(path.join(tmpdir(), "release-plan-"))
    const origin = path.join(root, "origin.git")
    work = path.join(root, "work")
    git(root, "init", "--bare", "--initial-branch=main", origin)
    git(root, "clone", origin, work)
    for (const name of ["one", "two", "three", "four"]) {
      git(work, "commit", "--allow-empty", "-m", name)
      commits.push(git(work, "rev-parse", "HEAD"))
    }
    git(work, "tag", "-a", "v1.0.0", commits[1], "-m", "v1.0.0")
    git(work, "tag", "nightly", commits[2])
    git(work, "push", "origin", "main", `${commits[1]}:refs/heads/release/1.0`)
  })

  after(() => rmSync(root, { recursive: true, force: true }))

  const repo = (releases = ["v1.0.0"]) =>
    createGitRepo({ cwd: work, releaseExists: (tag) => releases.includes(tag) })

  it("plans a minor release from the head of main", () => {
    assert.deepEqual(planRelease({ bump: "minor", refName: "main" }, repo()), {
      tag: "v1.1.0",
      previous: "v1.0.0",
      branch: "release/1.1",
      sha: commits[3],
      resume: false,
    })
  })

  it("resolves an abbreviated commit on main", () => {
    const result = planRelease(
      { bump: "minor", refName: "main", commit: commits[2].slice(0, 7) },
      repo()
    )
    assert.equal(result.sha, commits[2])
  })

  it("rejects a commit from before the last release", () => {
    assert.throws(
      () =>
        planRelease(
          { bump: "minor", refName: "main", commit: `${commits[2]}~2` },
          repo()
        ),
      /predates v1\.0\.0/
    )
  })

  it("refuses a release branch that already exists on origin", () => {
    git(work, "push", "origin", `${commits[3]}:refs/heads/release/2.0`)
    assert.throws(
      () => planRelease({ bump: "major", refName: "main" }, repo()),
      /release\/2\.0 already exists/
    )
  })

  it("resumes a pushed tag that has no release", () => {
    git(work, "tag", "-a", "v1.1.0", commits[3], "-m", "v1.1.0")
    try {
      const result = planRelease({ bump: "minor", refName: "main" }, repo())
      assert.equal(result.tag, "v1.1.0")
      assert.equal(result.resume, true)
    } finally {
      git(work, "tag", "-d", "v1.1.0")
    }
  })
})
