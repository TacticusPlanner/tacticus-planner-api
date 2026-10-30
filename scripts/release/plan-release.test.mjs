// Run with: node --test scripts/release/

import assert from "node:assert/strict"
import { describe, it } from "node:test"

import {
  ReleaseError,
  latestTag,
  parseVersion,
  planRelease,
} from "./plan-release.mjs"

/**
 * A fake repository: `commits` is the history of the current branch, oldest first, so a commit
 * is an ancestor of every commit after it. `tags` maps tag name to commit.
 */
function fakeRepo({
  commits = ["c1", "c2", "c3"],
  tags = {},
  remoteBranches = [],
  releases = Object.keys(tags),
} = {}) {
  const index = (ref) => commits.indexOf(tags[ref] ?? ref)
  return {
    head: () => commits.at(-1),
    resolveCommit: (ref) => (commits.includes(ref) ? ref : null),
    isAncestor: (a, b) => index(a) !== -1 && index(a) <= index(b),
    tags: () => Object.keys(tags),
    tagsAt: (sha) => Object.keys(tags).filter((tag) => tags[tag] === sha),
    remoteBranchExists: (branch) => remoteBranches.includes(branch),
    releaseExists: (tag) => releases.includes(tag),
  }
}

const plan = (inputs, repo = fakeRepo()) =>
  planRelease({ refName: "main", ...inputs }, repo)

const rejects = (inputs, repo, message) =>
  assert.throws(() => plan(inputs, repo), {
    name: "ReleaseError",
    message,
  })

describe("parseVersion", () => {
  it("accepts only strict vX.Y.Z tags", () => {
    assert.deepEqual(parseVersion("v1.12.3"), { major: 1, minor: 12, patch: 3 })
    for (const tag of ["1.2.3", "v1.2", "v1.2.3-rc.1", "v1.2.3.4", "release"]) {
      assert.equal(parseVersion(tag), null, tag)
    }
  })
})

describe("latestTag", () => {
  it("compares numerically, not as text", () => {
    assert.equal(latestTag(["v1.9.0", "v1.10.0", "v1.2.0"]), "v1.10.0")
  })

  it("ignores tags that are not versions", () => {
    assert.equal(latestTag(["v2.0.0-rc.1", "nightly", "v1.0.0"]), "v1.0.0")
  })

  it("returns an empty string when nothing matches", () => {
    assert.equal(latestTag([]), "")
  })
})

describe("planRelease: first release", () => {
  it("major gives v2.0.0 on release/2.0", () => {
    assert.deepEqual(plan({ bump: "major" }), {
      tag: "v2.0.0",
      previous: "",
      branch: "release/2.0",
      sha: "c3",
      resume: false,
    })
  })

  it("minor gives v1.1.0 on release/1.1", () => {
    const result = plan({ bump: "minor" })
    assert.equal(result.tag, "v1.1.0")
    assert.equal(result.branch, "release/1.1")
  })
})

describe("planRelease: minor and major", () => {
  const repo = () =>
    fakeRepo({
      commits: ["c1", "c2", "c3", "c4", "c5"],
      tags: { "v1.3.0": "c1", "v1.3.2": "c2" },
    })

  it("minor cuts the next release branch from the head of main", () => {
    assert.deepEqual(plan({ bump: "minor" }, repo()), {
      tag: "v1.4.0",
      previous: "v1.3.2",
      branch: "release/1.4",
      sha: "c5",
      resume: false,
    })
  })

  it("major resets minor and patch", () => {
    const result = plan({ bump: "major" }, repo())
    assert.equal(result.tag, "v2.0.0")
    assert.equal(result.branch, "release/2.0")
  })

  it("bases the version on the highest tag, even a patch on an older line", () => {
    const result = plan(
      { bump: "minor" },
      fakeRepo({ tags: { "v1.4.0": "c1", "v1.3.5": "c2" } })
    )
    assert.equal(result.tag, "v1.5.0")
    assert.equal(result.previous, "v1.4.0")
  })

  it("must run from main", () => {
    rejects(
      { bump: "minor", refName: "release/1.3" },
      repo(),
      "Run minor releases from main."
    )
  })

  it("refuses when the release branch already exists", () => {
    rejects(
      { bump: "minor" },
      fakeRepo({ remoteBranches: ["release/1.1"] }),
      "release/1.1 already exists."
    )
  })
})

describe("planRelease: releasing an older commit", () => {
  const repo = () =>
    fakeRepo({
      commits: ["c1", "c2", "c3", "c4", "c5"],
      tags: { "v1.3.0": "c2" },
    })

  it("ships the given commit instead of the head of main", () => {
    const result = plan({ bump: "minor", commit: "c4" }, repo())
    assert.equal(result.sha, "c4")
    assert.equal(result.tag, "v1.4.0")
  })

  it("refuses an unknown commit", () => {
    rejects(
      { bump: "minor", commit: "nope" },
      repo(),
      "nope is not a commit in this repository."
    )
  })

  it("refuses a commit that is not on main", () => {
    const branched = repo()
    branched.resolveCommit = (ref) => ref
    rejects({ bump: "minor", commit: "side" }, branched, "side is not on main.")
  })

  it("refuses a commit from before the last release cut", () => {
    rejects(
      { bump: "minor", commit: "c1" },
      repo(),
      "c1 predates v1.3.0; pick a commit after the last release."
    )
  })

  it("is not allowed for patches", () => {
    rejects(
      { bump: "patch", refName: "release/1.3", commit: "c4" },
      repo(),
      "commit applies to minor/major releases only; patch ships the head of the release branch."
    )
  })
})

describe("planRelease: patch", () => {
  const repo = () =>
    fakeRepo({
      tags: { "v1.4.0": "c1", "v1.4.1": "c2", "v1.5.0": "x" },
    })

  it("tags the next patch on the release branch", () => {
    assert.deepEqual(plan({ bump: "patch", refName: "release/1.4" }, repo()), {
      tag: "v1.4.2",
      previous: "v1.4.1",
      branch: "",
      sha: "c3",
      resume: false,
    })
  })

  it("only counts tags on the same release line", () => {
    const result = plan(
      { bump: "patch", refName: "release/1.4" },
      fakeRepo({ tags: { "v1.4.0": "c1", "v1.40.3": "x" } })
    )
    assert.equal(result.tag, "v1.4.1")
  })

  it("must run from a release branch", () => {
    rejects(
      { bump: "patch" },
      repo(),
      "Run patch releases from a release/X.Y branch."
    )
  })

  it("refuses a release branch with nothing to patch", () => {
    rejects(
      { bump: "patch", refName: "release/1.9" },
      repo(),
      "release/1.9 has no v1.9.* tag to patch."
    )
  })
})

describe("planRelease: rerun after the tag was pushed", () => {
  it("resumes a tag that has no GitHub release yet", () => {
    const repo = fakeRepo({
      tags: { "v1.0.0": "c1", "v1.0.1": "c2", "v1.1.0": "c3" },
      releases: ["v1.0.0", "v1.0.1"],
    })
    assert.deepEqual(plan({ bump: "minor" }, repo), {
      tag: "v1.1.0",
      previous: "v1.0.1",
      branch: "",
      sha: "c3",
      resume: true,
    })
  })

  it("resumes the first release with no previous tag", () => {
    const repo = fakeRepo({ tags: { "v1.0.0": "c3" }, releases: [] })
    const result = plan({ bump: "major" }, repo)
    assert.equal(result.resume, true)
    assert.equal(result.previous, "")
  })

  it("refuses a commit that is already released", () => {
    rejects(
      { bump: "minor" },
      fakeRepo({ tags: { "v1.0.0": "c3" } }),
      "c3 is already released as v1.0.0."
    )
  })
})

describe("planRelease: inputs", () => {
  it("rejects an unknown bump", () => {
    assert.throws(() => plan({ bump: "huge" }), ReleaseError)
  })
})
