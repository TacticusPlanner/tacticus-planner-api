## Context

See `proposal.md` - Why. Companion apps change: `goals-overview-v1-parity` in
`tacticus-planner-apps` (client dataset key/schema and the goal cost
calculation); this API half applies first.

The catalog pipeline (raw embedded JSON -> denormalizer -> served view ->
hashed manifest) already has the exact precedent: `mow-upgrade-costs` is a
flat raw array in `Data/`, projected by `MowDenormalizer.BuildMowUpgradeCosts`
into level-keyed views (`level = index + 2`), served by
`GetGameCatalogMowUpgradeCostsEndpoint`, and registered in
`GameCatalogDatasets` as both a raw `Required` key and a `Served` key.
V1's source is `tacticusplanner/src/data/characters-lvl-up-abilities.json`:
59 rows of `{ lvl, gold, badges }`, where `lvl` N is the cost of going from
level N to N + 1; rarity is not stored, `CharactersAbilitiesService` derives it
from the level raised to.

## Goals / Non-Goals

**Goals:**

- Serve the ladder with the same conventions as `mow-upgrade-costs`, so the
  client dataset plumbing is a copy of an existing pattern.
- Send rarity as a served value so the client does not reproduce V1's bands.

**Non-Goals:**

- No per-character or per-alliance variation: the ladder is shared, as in V1.
  Which alliance's badge icon to draw is the client's job (it knows the unit).
- No forge badges or components for characters (they exist only for MoWs).
- No change to existing datasets or the goals API.

## Decisions

1. **Raw file mirrors V1's shape; derive rarity at denormalization.** The raw
   `character-ability-costs.json` is V1's array copied unchanged (`lvl`,
   `gold`, `badges` as a number), keeping the port verifiable against V1
   line for line; a `CharacterAbilityCostDenormalizer` builds the served view
   (`level = lvl + 1`, `badges = { rarity, amount }`). *Alternative:*
   pre-shape the raw file to the served form (with rarity baked in); rejected
   because it makes the port unverifiable against V1 and duplicates a rule
   that is a pure function of `level`.
2. **Served badge shape reuses `GameCatalogAmountByRarity`** (`rarity` +
   `amount`), the same record `mow-upgrade-costs` uses, so the client can
   share one badge-amount schema.
3. **Rarity bands live in one static method next to the denormalizer,** with
   a unit test asserting each boundary (8/9, 17/18, 26/27, 35/36, 50/51)
   against V1's `getRarityFromLevel`.
4. **Validation follows the existing per-dataset validators:** non-empty,
   consecutive `lvl` from 1, gold >= 0, badges > 0. It runs over the raw
   snapshot collection like the others, and `ManifestValidation` already
   requires every served dataset to be non-empty, so registering the served
   key is enough.
5. **`SchemaVersion` is not bumped.** A new dataset is additive: existing
   clients ignore an unknown manifest entry and no served shape changes.
   *Alternative:* bump it to force clients to refetch; rejected because
   content changes already ride the per-dataset hash. Confirm against the
   manifest snapshot test when implementing.
6. **No EF Core migration.** The catalog is embedded data, not the database.

## Risks / Trade-offs

- [The client ships before the API is deployed] -> the apps spec requires the
  client to degrade to "no Character Ability chips" when the dataset is
  missing, so mismatched deploy order is harmless; API-first is still the
  order.
- [V1's table changes with a game patch] -> it is a plain raw JSON edit plus a
  hash change; no code change.
- [Rarity bands are V1's, not from game data] -> they are the only source we
  have; the boundary test pins them and a comment cites V1.
