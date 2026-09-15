## Context

See `proposal.md` and the delta spec. `guild-raid-meta.json` currently authors broad Comp profiles plus exact boss recommendations. The loader validates raw references, the denormalizer emits an id-only view, and the manifest hashes the served payload. An audit found exact recommendations whose heroes or Machines of War are outside their referenced Comp pools, so Comp membership cannot become an implicit substitution rule.

The companion `tacticus-planner-apps` change consumes the expanded `guild-raid-meta` served dataset before the later playable-variants change uses it.

## Goals / Non-Goals

**Goals:**

- Make every allowed substitution explicit at the exact-team slot where it applies.
- Preserve stable identity and deterministic editorial ordering through raw data, validation, denormalization, and serving.
- Keep the payload structural/id-only and fail fast on unsafe rules.

**Non-Goals:**

- Computing a player-specific lineup on the server.
- Encoding effectiveness weights, minimum investment, damage, synergy, or strategy prose.
- Reinterpreting existing Comp core/flex/Machine-of-War lists as substitutions.

## Decisions

### Extend exact recommendations instead of creating a parallel rules dataset

Each raw/served recommendation gains `id`, `heroSlots`, and `mowReplacementIds`. A slot repeats its aligned ideal `heroId` and adds `roleId`, `essential`, and ordered `replacementCharacterIds`. Co-locating the rule with the exact recommendation prevents a second join key/path and makes one recommendation the canonical structure from which clients derive ideal and playable views.

A separate replacement graph was rejected because boss/tier applicability and exact-slot context would have to be duplicated and reconciled.

### Keep `heroIds` during this additive phase

The five existing `heroIds` remain the canonical exact lineup for current clients. `heroSlots[i].heroId` must equal `heroIds[i]`; validation enforces this temporary duplication. Removing `heroIds` would be a breaking payload change and is unnecessary for the targeted feature.

### Author roles and availability structurally

`roleId` is a stable token, not display text. `essential` controls later Unavailable classification. Empty replacement arrays are meaningful. Machine-of-War alternatives are recommendation-level because there is one ideal MoW, not five slots. Ordering expresses editorial preference but carries no numeric score.

### Strengthen raw validation before denormalization

Validation covers globally unique recommendation ids, exactly five aligned slots, per-list uniqueness, exclusion of the ideal unit from its own alternatives, and character/Machine-of-War reference types. Cross-recommendation candidate reuse remains allowed. The denormalizer performs a direct ordered projection with no inferred data.

### Manifest and release semantics

Only `Data/guild-raid-meta.json`, `Models/GuildRaidMeta.cs`, `Denormalization/GuildRaidMetaDenormalizer.cs`, and `Validation/GuildRaidMetaValidation.cs` change in the catalog pipeline. The `guild-raid-meta` dataset hash and source hash change; other dataset hashes remain stable. The API manifest snapshot and catalog validation/denormalization tests are updated.

The new fields are additive, so `SchemaVersion` is not bumped under the repository's breaking-shape policy. `Version`/`GameVersion` are not bumped because this is curated editorial metadata rather than a game-data refresh.

### No persistence migration

The data is embedded catalog JSON and no EF entity changes. No migration, backfill, or database rollback is required.

## Risks / Trade-offs

- [Risk] Duplicated `heroIds` and `heroSlots.heroId` can drift → Loader validation requires exact positional equality.
- [Risk] Editorial rules accidentally allow impossible duplicate lineups → Per-rule uniqueness plus later client assignment constraints prevent a unit filling two slots.
- [Risk] Additive fields invalidate a newer client's cached older payload → The companion client treats missing rules as variants-unavailable while preserving exact Meta readiness until sync completes.
- [Risk] Stable role ids can outpace translations → The client uses readable fallbacks and translations can be added without catalog changes.

## Migration Plan

Apply and deploy the API catalog expansion first. Review the manifest snapshot to confirm that only `guild-raid-meta` and the aggregate source hash changed. Then apply the companion client schema/persistence change. Rollback restores the prior embedded JSON/models; no database state is involved.
