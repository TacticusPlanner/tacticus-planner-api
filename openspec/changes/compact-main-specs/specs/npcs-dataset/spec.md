## MODIFIED Requirements

### Requirement: The catalog serves one flat `npcs` dataset built from the per-faction raw sources

The game catalog SHALL serve a dataset keyed `npcs` built from the pinned datamine copy `gameconfig/units/npc.json` (keyed by NPC id), with damage profiles from `gameconfig/units/abilities.json`. The catalog SHALL keep no hand-kept NPC files. NPCs SHALL be grouped per faction under the source key `npcs-<faction>` (lowercased `FactionId`); records without a `FactionId` (loot objects) form the `npcs-objects` bucket. The served dataset SHALL be a single flat list of NPC records ordered by source key (ordinal) and then by id (ordinal).

#### Scenario: Records from every raw source are present

- **WHEN** the datamine `npc.json` at `v1.43.157.2989` holds 539 NPCs across 20 factions plus loot objects
- **THEN** every one appears exactly once in the served list, in 21 source groups

## REMOVED Requirements

### Requirement: Adding the classification and faction fields does not change the dataset's identity or schema version

**Reason**: L4 — one-off transition rule ("The change SHALL…", "after this change", "pre-change hash") that only applied while the fields were added.

**Migration**: None. The fields are specified by "Each NPC record is classified as a unit, Machine of War, or loot object"; the manifest entry by `game-catalog-http-caching` — "Every served dataset is public and listed in the manifest".
