#!/usr/bin/env node

// Entry point for the Release workflow's "Compute version" step. Reads BUMP, REF_NAME and
// COMMIT from the environment and writes tag, previous, branch, sha and resume to
// $GITHUB_OUTPUT. Run locally (outputs go to stdout) from a full clone:
//   BUMP=minor REF_NAME=main node scripts/release/compute-version.mjs

import { appendFileSync } from "node:fs"

import { createGitRepo } from "./git-repo.mjs"
import { ReleaseError, planRelease } from "./plan-release.mjs"

try {
  const plan = planRelease(
    {
      bump: process.env.BUMP,
      refName: process.env.REF_NAME,
      commit: process.env.COMMIT ?? "",
    },
    createGitRepo()
  )
  if (plan.resume) {
    console.log(
      `::notice::Resuming ${plan.tag}, which was tagged but never published.`
    )
  }
  const output = Object.entries(plan)
    .map(([key, value]) => `${key}=${value}\n`)
    .join("")
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, output)
  } else {
    process.stdout.write(output)
  }
} catch (error) {
  if (!(error instanceof ReleaseError)) {
    throw error
  }
  console.log(`::error::${error.message}`)
  process.exitCode = 1
}
