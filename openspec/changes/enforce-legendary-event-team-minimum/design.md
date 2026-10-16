# Design

## Context

`LegendaryEventCatalogValidator.ValidateUnits` rejects `members.Count is 0 or > MaxMembers` ("A team has 1–5 members"), `MaxMembers = 5` lives in `LegendaryEventValidation`, and the EF configuration constrains member positions to `0..MaxMembers − 1`. The V1 import (`V1LegendaryEventImportService`) truncates above five (`team_truncated`) and skips only empty teams (`empty_team`). The reserve is stored as a member row with `Reserve = true` and is not part of `memberUnitIds`. Companion apps change: `enforce-legendary-event-team-minimum` in `tacticus-planner-apps` (capability `legendary-event-team-size`). See proposal.md for the evidence.

## Goals / Non-Goals

**Goals:**

- Make an invalid team impossible to write through the API and the import.
- Keep existing data readable and removable without a migration.

**Non-Goals:**

- Deleting or rewriting stored short teams (the user decides; one such team exists on staging).
- Constraining the reserve, or requiring the objective set to be non-empty.
- A database check constraint (the team row and member rows are written in one request and the check would also catch legacy rows during unrelated updates).

## Decisions

**D1. Enforce on write only, in the existing validator.** `MinMembers = 3` beside `MaxMembers`; `ValidateUnits` fails with `memberUnitIds` and the message "A team has 3–5 members". Every create and update path already calls it. Alternative: a database check constraint. Rejected: it would also reject legacy rows on unrelated writes (depth, order) and require a data fix first.

**D2. Legacy short teams stay valid for reads, deletes and reorder.** Reorder and delete do not touch members, so they bypass `ValidateUnits`; only a team write that carries members is checked. A depth write that re-sends a legacy short team's two members is rejected, which is the intended pressure to complete it; the apps hide the depth stepper for such a team (apps design D2).

**D3. The import skips short teams instead of padding or merging them.** Padding would invent members; merging with another team changes the plan. The skip is reported with `team_too_small` and the remaining count so the result can say how many units were missing. `empty_team` keeps meaning zero units.

**D4. The reserve never counts.** The rule is about the characters that fight; a reserve is a spare. Alternative: count the reserve. Rejected: a team of two plus a reserve would then be valid, which the game does not allow.

## Risks / Trade-offs

- [A client that still sends short teams gets 400s] → The apps half ships the editor rule in the same release pair; both are internal, and the apps already validate before sending.
- [One existing short team cannot be re-saved until completed] → Intended; the apps explain it ("Needs at least 3 characters") and offer Edit and Delete.
- [The import now drops teams V1 users built with two units] → They appear in the import result with `team_too_small` and the count, so users know which team to rebuild.

## Migration Plan

No migration. Deploy this half before the apps half; during the gap the old apps editor can still produce a short team, which this API now rejects with a 400 the old client shows as a generic save error. Roll back by reverting the constant and the validator/import changes.
