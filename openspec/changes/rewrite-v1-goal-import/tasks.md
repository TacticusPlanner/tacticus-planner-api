## 1. Prerequisites from other changes

- [ ] 1.1 Confirm `fix-goal-mutation-isolation-level` is applied, by checking
      the locked mutation runs at `READ COMMITTED` and its PostgreSQL
      concurrency test passes
- [ ] 1.2 Confirm `fix-goal-ability-cap-effective-progression` is applied, by
      running its test that an above-cap Ability target is accepted alongside a
      depended-upon Ascension spec

## 2. Parse what V1 actually sends

- [ ] 2.1 Extend the V1 goal DTO with the shard-source fields it currently
      omits (shard farm type, campaign usage, mythic campaign usage) and verify
      with a deserialization test against a captured V1 profile payload
- [ ] 2.2 Add a translation from those fields to acquisition sources, and
      verify with unit tests for Onslaught-only, energy-only, combined, and
      no-campaign-usage inputs
- [ ] 2.3 Omit an acquisition-source kind that is not valid for the target
      entity and goal type without failing the goal, and verify with a
      Machine-of-War test that the goal is still created

## 3. Progression rules needed for prerequisites

- [ ] 3.1 Add rank-to-minimum-progression (and its max-rank-for-progression
      inverse) to `ProgressionRules`, mirroring the client's table; verify with
      unit tests covering every rarity boundary
- [ ] 3.2 Add rank-to-required-character-level plus the decode of the rank
      target's partial-upgrade variant, mirroring the client; verify with unit
      tests at the ladder's ends and one mid-ladder partial-upgrade case
- [ ] 3.3 Add ability-level-to-minimum-progression as the inverse of the
      existing ability-cap table; verify with a round-trip test against that
      table
- [ ] 3.4 Cross-check all three tables against the client's equivalents and
      verify the values match exactly, so the deliberate duplication cannot
      drift silently

## 4. Refuse goals without player data

- [ ] 4.1 Refuse the goals part when the account has no player data snapshot,
      creating no goals and reporting one blocking outcome; verify with an
      endpoint test asserting zero goals created
- [ ] 4.2 Verify with a test that the other selected parts still process
      normally when the goals part is refused
- [ ] 4.3 Remove the bottom-of-the-ladder defaulting from the translator's
      already-reached checks, now that a snapshot is guaranteed; verify by
      review that no resolver silently substitutes a floor value

## 5. Server-side creation

- [ ] 5.1 Build each goal's initial-state snapshot server-side from the loaded
      player data; verify with a test that a created goal's snapshot carries
      the unit's rank, progression and ability levels
- [ ] 5.2 Carry V1 goal notes onto the created goal, and merge the notes of
      every contributing source goal when duplicates merge; verify with tests
      for both
- [ ] 5.3 Replace the returned goal specs with server-side creation inside one
      `ExecuteLockedMutationAsync` over the default project, staging all
      survivors and calling `SaveChanges` and `NormalizeAsync` once each;
      verify with a test that goals exist when the operation returns
- [ ] 5.4 Decide each goal's fate by read-only checks (target validation, slot
      conflict) so a rejection never dirties the transaction; verify with a
      test that a profile containing one rejected goal still creates the rest
- [ ] 5.5 Add a marked shortcut comment noting that target validation re-reads
      the player data snapshot per goal inside the lock, naming the ceiling and
      the upgrade path; verify by review

## 6. Outcome reporting

- [ ] 6.1 Replace the goal specs, skipped count, and issue list in the response
      with one outcome per source V1 goal, in V1 priority order, carrying
      status, code, message, entity type and id, goal type, and created or
      matched goal id; verify with a test asserting the outcome count equals
      the source goal count
- [ ] 6.2 Classify already-reached, already-exists, and merged as skipped, each
      with its own code; verify with a test per case
- [ ] 6.3 Classify unsupported V1 goal type, unknown unit, off-ladder target,
      and missing target as not imported, each with its own code; verify with a
      test per case
- [ ] 6.4 Carry the V1 unit identifier as it appeared in the profile on an
      unknown-unit outcome, so the failed match is identifiable; verify with a
      test
- [ ] 6.5 Report the previously silent Rank and Ascension merges as skipped
      outcomes carrying the surviving goal's id; verify with a test that a
      two-rank-goal profile yields one created and one merged outcome
- [ ] 6.6 Ensure the unsupported-goal-type message makes no promise of future
      support; verify by reading the message text
- [ ] 6.7 Verify with a test that a created outcome count equals the number of
      V2 goals created from source goals, and that no response field mixes
      source goals with unit specs

## 7. Prerequisite synthesis

- [ ] 7.1 Add the selection flag for automatic prerequisite creation,
      defaulting on; verify with tests that goals import with and without it
- [ ] 7.2 Compute each unit's required progression and required level from its
      imported targets, evaluating live player state first so an already-met
      requirement synthesizes nothing; verify with unit tests
- [ ] 7.3 Synthesize an Unlock goal when the unit is absent from the roster and
      an imported goal requires it, skipping it when the unit's imported goals
      or the account already have one; verify with tests for all three cases
- [ ] 7.4 Synthesize an Ascension goal at the minimum satisfying target, and a
      Level goal for characters at the minimum satisfying level; verify with
      tests including one where a single prerequisite covers two imported goals
- [ ] 7.5 Declare each synthesized prerequisite as a dependency of every
      imported goal that required it, emitting them before those goals so
      dependency references point strictly earlier; verify with a test on the
      created goals' dependency edges
- [ ] 7.6 Report an imported Ascension goal whose target falls short of what
      another imported goal needs, without altering it; verify with a test
      asserting the target is unchanged and the shortfall is reported
- [ ] 7.7 Report each synthesized prerequisite as its own outcome, identified as
      automatically added and naming the source goal it unblocks; verify with a
      test
- [ ] 7.8 Verify with a test that the synthesized rule set and targets match
      what the manual create-goal flow produces for the same unit and target,
      so the parity claim is checked rather than asserted

## 8. Ordering

- [ ] 8.1 Create goals in ascending V1 priority order with consecutive
      membership priorities; verify with a test that unit blocks appear in the
      order their units first appear in V1 priority order
- [ ] 8.2 Add a PostgreSQL-backed test that a multi-unit import yields
      contiguous in-flight priorities from 1 with no gaps or duplicates, and
      each unit's goals contiguous
- [ ] 8.3 Verify with a test that a prerequisite precedes the goals depending
      on it within its unit block

## 9. Idempotency

- [ ] 9.1 Verify with a test that importing the same V1 profile twice creates
      no duplicate goals and reports the second run's goals as skipped because
      they already exist, carrying the existing goal ids

## 10. Contract and coordination

- [ ] 10.1 Build, confirm the regenerated `artifacts/openapi` artifact reflects
      the new import response schema and no longer exposes the goal specs,
      skipped count, or issue list, and commit the artifact
- [ ] 10.2 Verify the companion `tacticus-planner-apps` change of the same name
      exists with tasks covering the new response, removal of the client-side
      fan-out and snapshot resolution, the bucketed outcome report, and the
      missing `Ability -> Ascension` prerequisite edge

## 11. Live verification

- [ ] 11.1 Start the stack through Aspire, wait for `api` and `api-migrations`
      to report healthy, and import a real V1 profile with goals against an
      account that has synced player data; verify the outcome count equals the
      V1 goal count and every created goal exists
- [ ] 11.2 Against the same stack, import goals for an account with no synced
      player data and verify no goals are created and the goals part reports
      the sync-required outcome
- [ ] 11.3 Against the same stack, import a V1 profile containing an
      Onslaught-only ascension goal and verify the created goal carries an
      Onslaught acquisition source and no Campaign source

## 12. Repository gates

- [ ] 12.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
      reports no changes
- [ ] 12.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore` succeeds
- [ ] 12.3 `dotnet test TacticusPlanner.slnx -c Release --no-build` passes

## 13. Deferred / out-of-session

- [ ] 13.1 End-to-end verification against a large real V1 profile (near V1's
      hundred-goal limit) needs a volunteer account from a reporter. Track as
      an issue and record the link here; do not check off 11.1 in its place
