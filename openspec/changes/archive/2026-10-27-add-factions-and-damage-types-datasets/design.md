# Design

## Context

`gameconfig/units/factions.json` maps 23 faction ids to `{ unit, traits, unlockTimestamp, collectionFaction? }`. `unit`
is always empty; `traits` is always one alliance; `collectionFaction` is set only on `AdeptusAstartes` and
`TheLostAndTheDamned`, the collection factions that group units of other factions. `gameconfig/units/damageProfiles.json`
maps 22 damage types to `{ PiercingRatio, Traits? }`, with `PiercingRatio` a whole percentage (Physical 1, DirectDamage
100). Companion apps change: roadmap step 7 "Use `factions` and `damage-types`".

## Decisions

### D1. Name fields after the game

Per the roadmap, served words follow the game: `piercingRatio` stays a percentage (the apps divide by 100), `traits`
keeps the game's trait ids, and `collectionFaction` keeps the game's flag name. `traits` on a faction is served as the
single `alliance`, the word every other catalog dataset already uses for it.

### D2. Unlock date as UTC string

`unlockTimestamp` (Unix ms) is served as `unlockDateUtc` in `yyyy-MM-ddTHH:mm:ssZ`, the format the catalog already
uses for LRE run starts. The served catalog is hashed, so no date is compared with the clock.

### D3. Order

Factions are ordered by unlock time, then id: that is the game's own order and is stable. It is not the apps' display
order (`factionOrder` stays curated in the apps). Damage types have no meaningful game order, so they are ordered by id.

### D4. Cross-checks against the units

A faction or damage type a unit uses but the dataset lacks would leave the apps without an alliance or piercing ratio.
Validation fails startup on either, and on an alliance that disagrees with the unit's own. NPCs of kind `object` (the
faction-less loot bucket) are skipped.

### D5. One SchemaVersion bump

Adding served datasets is a contract addition, so `SchemaVersion` goes 10 → 11 like `unit-power` did. The apps' sync
skips dataset keys it does not know, so the current apps keep working.
