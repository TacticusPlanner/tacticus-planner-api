## Context

See `proposal.md` for the observed failure. The endpoint already pays the network cost of fetching a complete upstream player response, and the transformer already produces canonical hashes for every normalized chunk. However, an early return compares only the upstream game `configHash`, advances the successful-sync timestamp, and skips transformation and content comparison. The upstream contract separately supplies `lastUpdatedOn` as player-cache freshness metadata.

The client protocol is manifest driven: it downloads a chunk whenever the server advertises a hash different from its IndexedDB metadata. Correct server hashes are therefore sufficient to refresh existing web clients.

The game catalog has a related shard-estimation correctness gap. Raw campaign data can list the same shard in both guaranteed and potential rewards for one elite battle. Current denormalization emits those occurrences independently even though a raid grants them simultaneously.

## Goals / Non-Goals

**Goals:**

- Make transformed player content the authority for chunk identity.
- Preserve per-chunk write minimization and the current manifest/chunk wire contracts.
- Make the first post-fix sync self-heal server and client snapshots that were left stale by the old shortcut.
- Cover the distinction between game configuration, upstream player freshness, and normalized player content in endpoint tests.
- Represent simultaneous per-battle resource rewards as one farm location with their combined expected yield.

**Non-Goals:**

- Change the upstream Tacticus API caching policy or force it to refresh before its own `lastUpdatedOn` advances.
- Change the frontend delta-sync protocol, IndexedDB schema, or synchronization cadence.
- Add a database migration or bulk repair job.
- Change the public farm-location record shape or alter rewards for different resources or battles.

## Decisions

### Always transform a successfully fetched player response

Remove the `configHash`-only reuse path. After every successful upstream fetch, transform the response and calculate canonical content hashes before deciding what changed. The response has already been transferred, and transformation is small relative to the network request. Correctness therefore takes priority over avoiding local normalization and hashing.

**Alternative considered:** skip transformation when both `configHash` and `lastUpdatedOn` match. This could be valid if the upstream freshness timestamp is a perfect content version, but it reintroduces reliance on metadata semantics and saves little after the network call. It may be reconsidered only with explicit upstream guarantees and regression coverage.

### Retain per-chunk hash comparison as the write optimization

Load the tracked snapshot after transformation and invoke a chunk setter only when the newly computed hash differs from the stored hash. Read `configHash` and `lastUpdatedOn` from the upstream response. Update normalized chunk payloads and per-chunk hashes from the transformed result, and compute the aggregate source hash from transformed content plus `configHash`. Record the synchronization timestamp only after the snapshot update succeeds. A genuinely unchanged response therefore avoids rewriting chunk payloads while still recording a successful synchronization.

**Alternative considered:** replace every JSON chunk on every sync. Rejected because the existing hash comparison provides correct, inexpensive write minimization and avoids unnecessary PostgreSQL JSON updates.

### Keep configuration metadata in the aggregate source identity without using it as a content shortcut

Continue storing `configHash` and including it in the aggregate source hash so consumers can identify which game configuration contextualized the snapshot. Per-chunk hashes remain content-only, so a configuration-only change does not force clients to download unchanged chunks.

### Recover lazily on the next normal synchronization

No database migration is needed. Existing rows retain valid schema shapes; only their payloads and hashes may be stale. The next manual or scheduled sync transforms current upstream content, updates changed chunks, and returns a manifest that drives the existing client delta download.

### Consolidate repeated reward occurrences by resource and battle

When resolving locations for one resource, group raw occurrences by battle id. Preserve the existing representation for a single occurrence, including all resolved chance metadata for one probabilistic occurrence. For a group containing simultaneous occurrences, emit one location whose guaranteed flag is true when any occurrence is guaranteed and whose effective rate is the number of guaranteed occurrences plus each resolved potential effective rate. Set chance id, numerator, and denominator to null for every consolidated location because no single chance definition describes the combined rate.

This keeps the public catalog shape stable while establishing that one farm location represents one raid choice. It also prevents consumers from applying the same battle attempt cap independently to what are actually simultaneous rewards.

**Alternative considered:** leave split catalog entries and require every consumer to aggregate them. Rejected because the catalog would continue to encode one raid as multiple alternative locations and future consumers could repeat the same error.

## Risks / Trade-offs

- **[Risk] Increased CPU allocation on unchanged syncs** → Transformation and hashing run after every upstream fetch; retain selective persistence and measure only if synchronization volume makes this material.
- **[Risk] Upstream still serves cached player data** → Preserve `lastUpdatedOn` so diagnostics can distinguish a current local sync from the age of the upstream player snapshot; the service cannot manufacture data newer than the upstream response.
- **[Risk] Tests continue coupling content changes to config changes** → Add same-config fixtures that independently vary shards, unit progression, and upstream freshness.
- **[Risk] Combined rates cannot be expressed as one raw fraction** → Keep numerator and denominator null for a consolidated location and publish the already-supported effective rate.
- **[Risk] A battle repeats a guaranteed reward** → Count every authored guaranteed occurrence so the expected yield matches the raw reward list, and sum all resolved probabilistic components.

## Migration Plan

Deploy the endpoint and test changes without a schema migration. On the first successful sync after deployment, stale snapshots are reconciled and existing clients receive changed manifest hashes. Rollback is a normal code revert, although reverting would restore the stale-data behavior for later player changes.
