## Context

See proposal.md — Why. The raw NPC data is already loaded per faction into `GameCatalogFactionNpcs(Alliance, FactionId, Name, Npcs)`; only the flattening step (`GameCatalogDenormalizer.BuildNpcs`) loses that context, and `GameCatalogNpc` has no field to carry it. The raw JSON also carries an `icon` path per NPC that the record deliberately does not bind (id-only display contract — see the `game-catalog-data` skill).

Companion change: `add-npc-library-page` in `tacticus-planner-apps`. Shared contract surface: the served `npcs` dataset (`/api/v1/game-catalog/npcs`) and its `GameCatalogNpc` OpenAPI schema. The apps side tightens its zod `npcSchema` to require the three new fields, so this half must ship first.

## Goals / Non-Goals

**Goals:**
- Stamp `factionId` / `alliance` onto each served NPC from its owning raw file.
- Classify each served NPC as `unit` / `machineOfWar` / `object` on the server so no client string-matches ids.
- Keep the change additive: same key, endpoint, envelope, and `SchemaVersion`.

**Non-Goals:**
- Serving portraits, localized names, or variation labels — the client resolves those from `id`.
- Grouping variations into a "base NPC" — that is a presentation concept the client owns (grouping by name).
- Sorting or de-duplicating stat ladders — served in raw order; the client defines display order.
- Removing loot objects or Machines of War from the dataset — the raid-boss library resolves field NPCs and loot objects from this same list.

## Decisions

**D1 — Stamp faction on the flat record rather than serving a nested per-faction structure.**
`BuildNpcs` keeps its `SelectMany` but projects each NPC `with { FactionId = pair.Value.FactionId, Alliance = pair.Value.Alliance }`. Alternative: serve `npcsByFaction` as a keyed object like `units`. Rejected — the raid-boss denormalizer and `ReferenceValidation` (LRE wave enemies) already treat `npcs` as a flat id-keyed list, and the client indexes it by id (`getNpcsMap`); a nested shape would be a breaking `SchemaVersion` bump for no consumer benefit.

**D2 — `kind` derived from source file and the `MachineOfWar` trait, not from id spelling.**
The 11 Machine-of-War NPCs are exactly the 11 records carrying trait `MachineOfWar`; their ids spell the token `MoW` and `Mow` inconsistently. Objects are exactly the contents of `npcs-objects.json` (only 28 of 56 carry an `Object` trait, so the trait is not a reliable object marker). Precedent: `raid-bosses` derives `kind` (`boss` / `prime`) at denormalization. Alternative: let the client filter by `factionId === "Objects"` and `traits.includes("MachineOfWar")`. Rejected — the classification is a data fact the server already knows, and a served `kind` keeps the two-way rule in one reviewable place.

**D3 — Zero-stat "units" (`genesDecoy`, `astarNpc1HaywireMine`, tutorial dummies) stay `unit`.**
They are legitimate spawnable units with degenerate ladders. Introducing a fourth `kind` for them would encode a presentation preference in the data. The client hides variations whose ladder is all-zero.

**D4 — Additive change, no `SchemaVersion` bump.**
Every existing field keeps name, type, and meaning; the `npcs` dataset hash changes and the manifest snapshot is updated. Per `game-catalog-data`, `SchemaVersion` bumps only on a breaking shape change.

**D5 — Validation: assert `object` and `machineOfWar` are mutually exclusive.**
A record from `npcs-objects.json` carrying `MachineOfWar` would silently become `object` under the ordered rule; a validation error at load time makes the conflict visible instead. Added to the existing `GameCatalogValidator` partials.

## Risks / Trade-offs

- [The raw JSON already uses `icon`; a future datamine refresh could add other presentational keys] → Unbound properties are ignored on deserialize; the spec's "no presentational field" scenario is covered by the denormalizer test asserting the served record's property set.
- [OpenAPI artifact drifts from the model] → It regenerates on build; the task list includes verifying the regenerated `artifacts/openapi/TacticusPlanner.Api.json` shows the three new properties.
- [Client zod schema is a loose object today] → Extra fields pass through until the companion change tightens it, so deploying the API first cannot break the current client.

## Migration Plan

1. Merge and deploy this change; the manifest advertises the new `npcs` hash and clients re-download the dataset on next sync.
2. Apply the companion `tacticus-planner-apps` change.
3. Rollback: revert the API commit; the client's loose schema tolerates the old shape until the apps change ships, after which the apps change must be reverted together.
