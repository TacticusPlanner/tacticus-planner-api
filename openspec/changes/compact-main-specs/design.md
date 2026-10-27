# Design

## Context

See proposal.md, Why. The full audit is in `audit.md` next to this file: every finding with its file:line, the verbatim requirement header, and the exact old→new text. The deltas under `specs/` are that audit expressed as OpenSpec operations. This design records only the rules used to turn the audit into deltas and what was left out.

There is no raw dataset, served projection, manifest, EF Core migration or endpoint change. The companion change is `compact-main-specs` in `tacticus-planner-apps`. The two share no contract surface.

## Goals / Non-Goals

**Goals:**
- Each normative rule is stated once. Other mentions point to its owner (`see <capability>`).
- Main specs read as steady-state behavior, with no trace of the change that introduced them.

**Non-Goals:**
- Resolving contradictions. Where two copies disagree (D9, D10), or where the audit found an inconsistency (`user-settings` enum case sensitivity, `v1-goal-import` per-type skip, `/goals` vs `/me/goals` paths, catalog requirements hosted in `player-data-sync`), this change leaves both texts as they are. Choosing the right one is a behavior decision and gets its own change.
- Correcting stale raw-file names (L26, L27) or optional Purpose rewording (P5, P6).
- Splitting requirements over 500 characters. MODIFIED blocks keep their existing text apart from the audited cuts.

## Decisions

**Scope rule: no meaning changes.** An item is in scope only if removing it leaves every behavior still specified somewhere. The audit marks each item with its canonical copy. Items marked "differs" or "needs a decision" are out of scope (see Non-Goals). *Alternative:* fix contradictions in the same pass. Rejected because a reviewer could no longer approve the change as "deletions only".

**One added requirement instead of eight copies (D1).** The catalog-wide rule had no single owner, so the copies can only go if one owner is added. It goes in `game-catalog-http-caching`, whose Purpose already calls the catalog content-addressed and public. The code already serves every dataset route anonymously (`GetGameCatalogDatasetEndpoints.cs`), so this records existing behavior rather than new behavior. *Alternative:* a new `game-catalog-datasets` capability. Rejected as an extra capability for one requirement.

**OpenSpec operation per finding type:**
- A whole requirement duplicated or one-off → `REMOVED`. The **Reason** names the audit id and **Migration** names the canonical requirement, so archive keeps the trail.
- Sentence cuts → `MODIFIED` with the full block copied from the current main spec and only the audited text changed. Several findings in one requirement (e.g. S7+S8+L23, S5+L21) are merged into one MODIFIED block. Audit replacement scenarios (L8, L9, D1) are added to that block.
- Header renames (L21b) → `RENAMED`, then any MODIFIED of the same requirement uses the new name.

**Scenario drops and renames are direct edits made before archive.** OpenSpec 1.15 rejects a MODIFIED block that omits a scenario the main spec still has (`validate` and `specs-apply`), and it has no syntax for removing a single scenario. So the MODIFIED blocks keep every existing scenario. Every audited scenario drop or rename (S2–S9, the D1 boilerplate scenarios, L2, L7, L8, L9 "same as before", L20b, L25 and others) is listed in `direct-edits.md` by capability → requirement → scenario. During apply, before archive, a task removes or renames each one in the main spec and in the matching MODIFIED block, so the two agree and archive applies cleanly. *Alternative:* REMOVED plus ADDED of the whole requirement. Rejected because OpenSpec refuses the same header in both sections, and a rename-and-re-add would bury the real diff.

**Retiring `project-unit-ordering` (D15).** All four requirements are REMOVED, and `.openspec.yaml` sets `retire_capabilities: true`, so archive deletes the emptied capability. Nothing references it.

**Moving the orphan rule out of L5.** The removed "Adding the raid-bosses dataset does not bump the schema version" requirement held the only statement that `GameVersion` is the in-game build. That rule moves into `game-catalog-datamine-source` as an ADDED requirement matching current code (the trimmed `raw/VERSION`; loading fails when it is empty). This is the one ADDED requirement besides D1. Both record existing behavior.

**Purpose edits are direct.** Deltas ignore `## Purpose`, so P1–P4 and P7 are edited directly in `openspec/specs/` as a task in this change, applied in the same PR as the archive.

## Risks / Trade-offs

- [A MODIFIED block copied from a spec that changes before this lands silently reverts that change at archive] → Apply and archive soon after approval. A task re-diffs every MODIFIED block against the current main spec just before archive.
- [Removing a "duplicate" that actually carried a unique clause] → Each REMOVED entry names its canonical copy. Before archiving, the apply step greps for every behavior keyword of a removed requirement and checks the canonical copy still states it.
- [Replacement scenarios for retired-file comparisons pin counts (e.g. 119 characters) that change on the next datamine bump] → They are pinned to the named datamine version, as other dataset scenarios already are, so a bump changes that scenario together with the data.
