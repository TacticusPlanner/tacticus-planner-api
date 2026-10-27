# Tasks

## 1. Pre-flight

- [ ] 1.1 Re-diff every MODIFIED block under `specs/` against its current requirement in `openspec/specs/`. The only differences allowed are the ones `audit.md` calls for, plus the added replacement scenarios. Refresh any block whose main requirement changed since this change was written. Verify by listing the differences per block and checking each against an audit id.
- [ ] 1.2 For each REMOVED requirement, grep the canonical requirement named in its **Migration** for that requirement's key terms (e.g. `AllowAnonymous`/"without authentication", `goalRevisionStale`, slot conflict payload fields), and confirm the behavior is still stated there. Verify with a checklist of removed header → canonical header that has no gaps.

## 2. Direct edits to main specs

- [ ] 2.1 Apply every entry in `direct-edits.md`. Remove or rename each listed scenario in `openspec/specs/<capability>/spec.md`, and make the same edit in the matching MODIFIED block under `specs/` when one exists. Verify that every listed scenario title is absent (or renamed) in both places, using `grep -rn "#### Scenario: <title>"`.
- [ ] 2.2 Rewrite the Purpose paragraphs in `openspec/specs/` according to audit P1–P4 and P7:
  - P1: `game-catalog-datamine-source`, drop "Datasets move onto it one change at a time."
  - P2: `goal-target-model`, drop "after removal of independent equipment goals".
  - P3: `goal-bulk-status`, drop ", replacing the per-goal and per-project status routes".
  - P4: `project-goal-slots`, replace with the per-profile, per-target-key wording.
  - P7: `legendary-event-reward-ladder`, drop the "no shared cross-event ladder" clause.

  Verify that a grep for each removed phrase returns nothing.
- [ ] 2.3 Run `openspec validate compact-main-specs --strict` and `openspec validate --specs --strict` from `tacticus-planner-api`. Verify that both pass. Report any description-length warnings on untouched requirement text without fixing them in this change.

## 3. Archive dry check

- [ ] 3.1 Archive a throwaway copy of the change in a scratch worktree and diff the result against `main` (`git worktree add`, run `openspec archive compact-main-specs -y` there, then `git diff --stat`). Verify all of the following:
  - `project-unit-ordering/` is deleted.
  - The only spec files touched are the capabilities listed in proposal.md.
  - There are no stray `## ADDED/MODIFIED` headers in `openspec/specs/`.
  - Requirement count drops from 330 to the expected value (`grep -rh '^### Requirement:' openspec/specs | wc -l`).

  Then discard the worktree.

## Workflow follow-up

- Open one PR with the change folder and the direct main-spec edits. Archive in that PR once it is approved, so the copied MODIFIED blocks cannot go stale.
- Out-of-scope findings to file as separate work items: D9 (acquisition-source update scope), D10 (cap from existing Ascension dependencies), L26/L27 (stale raw-file names), and the four inconsistencies at the end of `audit.md`.
