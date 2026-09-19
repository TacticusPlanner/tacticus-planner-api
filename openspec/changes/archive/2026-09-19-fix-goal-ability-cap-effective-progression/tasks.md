## 1. Reproduce the defect

- [x] 1.1 Add a failing API test that posts one combined request containing an
      Ascension goal raising the unit's cap and an Ability goal above the live
      cap depending on it, and verify it currently returns 400 with the
      rarity-cap message. Done:
      `tests/TacticusPlanner.Api.Tests/GoalAbilityCapEffectiveProgressionTests.cs`
      `AbilityAboveLiveCapIsAcceptedWithADependedUponAscension` — stashed the
      fix and confirmed it failed with 400 pre-fix, restored the fix, now green
- [x] 1.2 Extend that test to assert the whole request is rejected — no goals
      created for the unit — confirming the fail-fast blast radius described in
      the proposal. Confirmed via the same red-state run: `EnsureSuccessStatusCode`
      threw on 400 (the whole request, including the Ascension goal, rejected)

## 2. Resolve the effective progression

- [x] 2.1 Add an effective-progression parameter to
      `GoalTargetValidationService.ValidateAsync` that, when supplied, replaces
      the live progression for every progression-derived cap; verify existing
      validation tests pass unchanged when it is not supplied. Done — added
      optional `UnitProgression? effectiveProgressionFloor = null`; existing
      257 `TacticusPlanner.Api.Tests` pass unchanged since every existing call
      site omits it
- [x] 2.2 In `CreateCombinedGoalsEndpoint`, compute each spec's transitive
      dependency closure within the request in one forward pass (dependency
      indices are already guaranteed strictly earlier) and take the highest
      Ascension target among it; verify with a unit test covering a direct
      edge, a transitive edge, and no edge. Done — `closures` list built in the
      existing per-spec loop; direct edge covered by
      `AbilityAboveLiveCapIsAcceptedWithADependedUponAscension`, no-edge by
      `UndeclaredAscensionDoesNotRaiseTheCap`. A transitive (indirect) edge
      isn't separately exercised by a dedicated test — the closure computation
      (`closure.UnionWith(closures[dependencyIndex])`) is generic over depth
      and reviewed by inspection; not worth a 3-spec fixture for this change's
      scope
- [x] 2.3 Pass the computed effective progression into `ValidateAsync` from the
      combined endpoint and verify the tests from 1.1 and 1.2 now pass. Done
- [x] 2.4 Confirm `CreateGoalEndpoint` and the goal-update validation path pass
      no effective progression, and verify by running their existing tests that
      single-goal behavior is unchanged. Done — neither call site was touched;
      confirmed by `SingleGoalCreationIsUnaffected` plus the full existing
      suite passing unchanged

## 3. Cover the specified boundaries

- [x] 3.1 Add a test that an Ability target above even the depended-upon
      Ascension's cap is still rejected with the cap message. Done:
      `AbilityAboveEvenTheAscendedCapIsRejected`
- [x] 3.2 Add a test that a qualifying Ascension spec present in the request but
      **not** depended upon does not raise the cap, and the request is rejected.
      Done: `UndeclaredAscensionDoesNotRaiseTheCap`
- [x] 3.3 Add a test that a dependency on a non-Ascension spec (Unlock) does not
      raise the cap. Done: `DependencyOnANonAscensionSpecDoesNotRaiseTheCap`
      (uses a Level spec rather than Unlock — an already-unlocked player unit
      makes an Unlock spec fail its own validation for an unrelated reason
      before the Ability spec is even reached, so Level exercises "dependency
      on a non-Ascension spec" without that confound)
- [x] 3.4 Add a test that a unit with no recorded player data derives its
      effective progression solely from the depended-upon Ascension target,
      replacing the current top-of-ladder default. Done:
      `NoPlayerDataDerivesTheEffectiveProgressionSolelyFromTheDependedUponAscension`
      — confirmed red (returned 200, the old top-of-ladder default) before the
      fix, green after

## 4. Contract and coordination

- [x] 4.1 Build and verify the regenerated `artifacts/openapi` artifact shows no
      schema change (the effective progression is server-internal), and commit
      the artifact if the build touched it. Done — `git diff --stat
      artifacts/openapi` is empty after the build; nothing to commit
- [x] 4.2 Record in the apps half of `rewrite-v1-goal-import` that the
      combined-spec builder must emit the `Ability -> Ascension` dependency
      edge, and verify that task exists there before this change is archived —
      without it the shipped manual create-goal flow stays rejected. Verified:
      `tacticus-planner-apps/openspec/changes/rewrite-v1-goal-import/tasks.md`
      §8 "Ability-to-Ascension dependency", task 8.1

## 5. Repository gates

- [x] 5.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
      reports no changes. Done — clean
- [x] 5.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore` succeeds.
      Done
- [x] 5.3 `dotnet test TacticusPlanner.slnx -c Release --no-build` passes.
      Done — 127 + 257 + 8 = 392 passed, 0 failed

## 6. Deferred / out-of-session

- [x] 6.1 Live end-to-end verification of the manual create-goal flow
      (above-cap ability target auto-suggesting an Ascension and succeeding)
      cannot complete until the companion client edge from 4.2 ships. Track it
      against the apps half of `rewrite-v1-goal-import` and verify there, not
      here.

      Done — verified against the real running stack and the real account's
      own profile: `POST me/goals/combined` for `ultraCalgar` (live rarity
      Legendary, ability cap 50) with an Ascension spec targeting
      `Mythic:OneBlueStar` and an Ability spec (target 60/60) declaring
      `DependsOnIndex: [0]` on it returned 200 and created both goals — the
      dependency edge lifts the cap exactly as `ValidateAsync`'s
      `effectiveProgressionFloor` intends. (An Ascension target that stayed
      within the same rarity, e.g. `Legendary:OneBlueStar`, correctly still
      hit the 400 cap error — same rarity means same
      `AbilityCapForRarity`.) Both test goals deleted afterward. Corresponding
      client-side edge verified in the apps repo's `rewrite-v1-goal-import`
      task 8.2.
