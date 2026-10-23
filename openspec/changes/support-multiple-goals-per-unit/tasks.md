## 1. Target identity

- [ ] 1.1 Add `GoalTargetKey.For(goalType, config)` in `TacticusPlanner.Domain/Goals` (Rank delegates to `RankTargetKey`; Ascension, Ability with `-` for unadvanced tracks, Upgrade sorted targets plus ranges; Unlock null), with unit tests for equal/unequal keys per type, including Ability unadvanced-track normalization and Upgrade ordering independence.
- [ ] 1.2 Switch `ProjectGoalPlanningService.SameSlot`, `GoalProblems.SendSlotConflictAsync` and `GoalEditing` to `GoalTargetKey`; verify existing Rank slot tests still pass unchanged.

## 2. Creation

- [ ] 2.1 Remove the "each goal type at most once" guard in `CreateCombinedGoalsEndpoint`; add endpoint tests: two Ascension specs with distinct ends succeed in one request, two with the same end return `slotOccupied`, a four-step Rank ladder with chained `DependsOnIndex` succeeds with contiguous priorities.
- [ ] 2.2 Add `CombinedGoalSpec.DependsOnGoalIds` (validator: distinct GUIDs; endpoint: in-flight, same profile, same entity, read under the lock); merge into `Goal.DependsOn`; lift `effectiveProgressionFloor` from existing Ascension dependencies; tests for an Ability 50 goal accepted when it depends on an existing Ascension→Legendary goal and rejected without it, and 400 for a foreign/completed/other-unit id.
- [ ] 2.3 Verify Unlock is still one per unit (single and combined creation).

## 3. Edits, status, import

- [ ] 3.1 Target edit: re-check the slot whenever `GoalTargetKey` changes for every keyed type; tests for Ascension/Ability/Upgrade collisions and a non-colliding edit.
- [ ] 3.2 Status transitions into Active/Paused use the new key; test resuming a Completed Ascension goal while a distinct-end one is in flight (succeeds) and a same-end one (409).
- [ ] 3.3 V1 import: group by `GoalTargetKey`; keep distinct Ascension/Ability/Upgrade goals and notes in V1 order; merge only exact duplicates; prerequisite synthesis reuses a covering existing/imported Ascension goal; update `v1-goal-import` tests (the "Duplicate ability goals keep the higher-priority one" scenario now applies only to equal targets).
- [ ] 3.4 V1 material goals: group by (unit, material) instead of one Upgrade goal per unit; remove `OccupiedUpgradeOutcome` and the "unit with no Upgrade goal yet" tie-break in suggested-unit selection; tests for different materials on one unit (separate goals, no manual step) and the same material (merged, larger quantity).
- [ ] 3.5 Archive `import-all-v1-goal-types` before archiving this change (this change modifies its material-goal requirement).

## 4. Contract and gates

- [ ] 4.1 Regenerate and inspect `artifacts/openapi` (`dependsOnGoalIds`, `slotOccupied.normalizedTarget` description); coordinate the companion apps change `support-multiple-goals-per-unit` (`api:pull`).
- [ ] 4.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`.
