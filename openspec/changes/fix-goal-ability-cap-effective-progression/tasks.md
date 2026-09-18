## 1. Reproduce the defect

- [ ] 1.1 Add a failing API test that posts one combined request containing an
      Ascension goal raising the unit's cap and an Ability goal above the live
      cap depending on it, and verify it currently returns 400 with the
      rarity-cap message
- [ ] 1.2 Extend that test to assert the whole request is rejected — no goals
      created for the unit — confirming the fail-fast blast radius described in
      the proposal

## 2. Resolve the effective progression

- [ ] 2.1 Add an effective-progression parameter to
      `GoalTargetValidationService.ValidateAsync` that, when supplied, replaces
      the live progression for every progression-derived cap; verify existing
      validation tests pass unchanged when it is not supplied
- [ ] 2.2 In `CreateCombinedGoalsEndpoint`, compute each spec's transitive
      dependency closure within the request in one forward pass (dependency
      indices are already guaranteed strictly earlier) and take the highest
      Ascension target among it; verify with a unit test covering a direct
      edge, a transitive edge, and no edge
- [ ] 2.3 Pass the computed effective progression into `ValidateAsync` from the
      combined endpoint and verify the tests from 1.1 and 1.2 now pass
- [ ] 2.4 Confirm `CreateGoalEndpoint` and the goal-update validation path pass
      no effective progression, and verify by running their existing tests that
      single-goal behavior is unchanged

## 3. Cover the specified boundaries

- [ ] 3.1 Add a test that an Ability target above even the depended-upon
      Ascension's cap is still rejected with the cap message
- [ ] 3.2 Add a test that a qualifying Ascension spec present in the request but
      **not** depended upon does not raise the cap, and the request is rejected
- [ ] 3.3 Add a test that a dependency on a non-Ascension spec (Unlock) does not
      raise the cap
- [ ] 3.4 Add a test that a unit with no recorded player data derives its
      effective progression solely from the depended-upon Ascension target,
      replacing the current top-of-ladder default

## 4. Contract and coordination

- [ ] 4.1 Build and verify the regenerated `artifacts/openapi` artifact shows no
      schema change (the effective progression is server-internal), and commit
      the artifact if the build touched it
- [ ] 4.2 Record in the apps half of `rewrite-v1-goal-import` that the
      combined-spec builder must emit the `Ability -> Ascension` dependency
      edge, and verify that task exists there before this change is archived —
      without it the shipped manual create-goal flow stays rejected

## 5. Repository gates

- [ ] 5.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
      reports no changes
- [ ] 5.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore` succeeds
- [ ] 5.3 `dotnet test TacticusPlanner.slnx -c Release --no-build` passes

## 6. Deferred / out-of-session

- [ ] 6.1 Live end-to-end verification of the manual create-goal flow
      (above-cap ability target auto-suggesting an Ascension and succeeding)
      cannot complete until the companion client edge from 4.2 ships. Track it
      against the apps half of `rewrite-v1-goal-import` and verify there, not
      here
