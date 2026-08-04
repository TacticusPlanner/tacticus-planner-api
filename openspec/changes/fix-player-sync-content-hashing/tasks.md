## 1. Correct player-content change detection

- [x] 1.1 Remove the `configHash`-only snapshot reuse path so every successful upstream player response is transformed and canonically hashed
- [x] 1.2 Preserve selective chunk persistence by comparing each transformed content hash with the stored chunk hash before invoking its setter
- [x] 1.3 Update configuration, upstream `lastUpdatedOn`, aggregate source, schema, chunk-hash, and successful-sync metadata consistently for both changed and unchanged content
- [x] 1.4 Update endpoint and domain documentation that currently describes `configHash` as a player-data change detector

## 2. Add sync-correctness regression coverage

- [x] 2.1 Extend the fake Tacticus client with independently variable player content, `configHash`, and `lastUpdatedOn` fixtures
- [x] 2.2 Add an endpoint test proving locked-character shard changes under an unchanged `configHash` update the `inventory-shards` payload and hash
- [x] 2.3 Add an endpoint test proving unlocked-unit progression changes under an unchanged `configHash` update only the affected roster chunk
- [x] 2.4 Add coverage proving an identical repeated response retains every chunk hash while advancing successful-sync metadata
- [x] 2.5 Add coverage proving upstream `lastUpdatedOn` is persisted independently from the game-configuration hash and a stale snapshot self-heals on the next normal sync

## 3. Verification

- [x] 3.1 Run the focused player-data transformer and sync endpoint tests
- [x] 3.2 Run strict OpenSpec validation, `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, the Release build and full test suite, and `git diff --check`
- [x] 3.3 Build the API Docker image using the repository Dockerfile
- [x] 3.4 In the Aspire-hosted signed-in stack, sync a profile whose stored shard snapshot is stale while the game configuration is unchanged; verify the server manifest changes, the web client downloads `inventory-shards`, and Unlock estimates match the in-game/V1 shard counts without clearing IndexedDB

## 4. Consolidate simultaneous catalog rewards

- [x] 4.1 Group occurrences of the same resource and battle during farm-location resolution, retaining single-occurrence semantics and emitting one combined effective rate for simultaneous rewards
- [x] 4.2 Add catalog regression tests proving elite guaranteed-plus-`shard_elite` locations such as FoCE40 and SHME40 are unique and yield `1.079` expected shards per raid
- [x] 4.3 Verify consolidated locations retain the existing public schema and update any affected catalog manifest snapshot

## 5. Reward-consolidation verification

- [x] 5.1 Run focused game-catalog tests, strict OpenSpec validation, formatting verification, the Release build, full tests, and `git diff --check`
