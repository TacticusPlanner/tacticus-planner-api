## Context

See proposal.md for motivation and `specs/npc-encounters-dataset/spec.md` for the served shape and
derivation table.

Today the knowledge of "which NPC fights where" is spread over these places:

```
 raw datamine                                   built today by                     served as
 ---------------------------------------------  ---------------------------------  --------------------
 battles/campaigns.json (+ campaign curation)   CampaignDatamineReader             campaign-battles / -definitions
 battles/battleSets.json  legendary_event_*     LreDatamineReader                  lre-battles
 liveEvents/linearHeroEvent.json                EventDatamineReader (quests)       quest-events
 liveEvents/mowEvent.json                       IncursionDenormalizer              incursions
 globalconfig/guildBoss/*                       RaidBossDenormalizer               raid-bosses
 battles/battleSets.json  survival_*            (nothing)                          -
 battles/onslaught.json   tierWaves             only rewards (onslaught-rewards)   -
 battles/battleSets.json  sync_pvp_*            (nothing; out of scope)            -
```

`GameCatalogLoader.Load()` builds every served view in memory before hashing, so a new dataset can be
built *from the finished views* of the others. Both raw files the two new sources need
(`battleSets.json`, `onslaught.json`) are already allowlisted in `Data/datamine/files.txt`.

## Goals / Non-Goals

**Goals:**
- One canonical inversion (NPC to encounters) computed once at load and hashed like every other dataset.
- Every `groupId` / `battleId` resolves in the dataset a client would link to.

**Non-Goals:**
- Event timing, "currently available" or "next run". The catalog hash must not depend on the clock (the
  same rule `lres` follows), and the client already has `events-calendar` and `lres` run starts.
- Enemy levels or stats per encounter (ladder indexes mean different things per source: campaign bosses
  are 1-based, other campaign NPCs 0-based and capped). That can come later as an added field.
- Onslaught wave probabilities, Sync PvP, tutorial-only content, past events missing from the datamine.
- Changing any existing dataset's shape.

## Decisions

### D1. Derive from served views, not by re-reading raw files

The campaign, Legendary Event, quest, incursion and guild raid encounters are computed from the
denormalized views (`campaign-battles` + `campaign-definitions`, `lre-battles`, `quest-events`,
`incursions`, `raid-bosses`) after they are built.

*Why:* those views already apply each source's curation and id rules (campaign battle id prefixes, the
served LRE events picked by `lre-curation.json`, quest battle numbering). Re-reading raw files would
repeat those rules and could name a battle id no served dataset has.
*Alternative:* a raw-text scan over every datamine file (what the exploration used). It is fast to
write, but it can't say *which* battle or track an NPC is in, and it picks up unrelated references.

Survival and Onslaught have no served battle view, so two small readers parse exactly the fields the
spec names (`survival_*` sets' `waves[].round` / `army`, and `tierWaves[].tier` / `round` / `enemies`).
They don't add served Survival or Onslaught battle datasets. That stays out of scope until a page needs one.

### D2. One record per (NPC, source, group), with typed battle entries

The served shape is a flat list grouped by `(npcId, source, groupId)`, and each battle entry is a single
uniform record with nullable `battleId` / `track` / `tier` / `number`, a `challenge` flag, a `role`,
`rounds` and `count`.

`count` is what the planner shows next to each NPC in a battle. Campaign, event and incursion views already
carry counts; an NPC at two ladder indexes is summed. Survival and Onslaught repeat a raw `npcId:N` entry per
unit, so the repeats are counted. A Survival record has one entry per round because the count differs per
round. An Onslaught tier keeps its largest wave because its waves are alternatives from a weighted pool.
Guild raids name no count.

`levels` lets a battle's NPC open at the level it fights at, in the NPC Library. About 1 in 5 NPC-battle pairs in
Legendary Events mix levels, so a level is a list, not a field. It is resolved here because each source numbers
levels differently. `campaign-battles` gains `servedIndex` on `detailedEnemyTypes` because the view drops the raw
index; a test checks it against the rank and stars the view already serves.

Building `levels` exposed that campaign enemies were read one row too high: the reader treated a non-boss index
as 0-based and capped it. Run through the app's power formula, a battle's enemies match its served `enemyPower`
far better one row lower (median error 23.3% to 2.6%, in every campaign type), and V1's battle cards also read
`index - 1`. Every campaign index is now 1-based. This changes the `rank` and `stars` of `campaign-battles` for
all non-boss enemies. The formula lives in the TypeScript domain package, so the API pins known-good rows instead.

*Why:* the UI groups encounters exactly this way ("Campaign Event: Adeptus Mechanicus, Extremis 3"), the
filter needs only the distinct `source`s, and one uniform C# record keeps the derived `schemaHash` and
the client zod schema simple.
*Alternatives:* (a) one record per battle, which repeats `npcId/source/groupId` for many rows and
makes the client regroup them; (b) a per-source polymorphic union, which is more precise but needs
polymorphic JSON in the contract hashing and the client schema for little gain.

### D3. Guild raid NPC identity through `questUnitId`

Guild raid units are their own unit sets with raid-specific stats, and `questUnitId` links them to an
NPC (`GuildBoss1Npc1TyranTermagantLeviathan` to `tyranNpc3Termagant`). A boss or prime unit set with a
`questUnitId` yields a `boss` / `prime` entry in its own group. Each encounter's `fieldNpcIds` (unit-set
keys) resolve through the merged unit-set map to `enemy` entries in the encounter's `unitSetId` group. A `questUnitId`
that is a served *character* (13 primes in `1.43.157.2989` are built on heroes, e.g. `astraPrimarisPsy`) is
skipped: the character is not an NPC. Only an id that is neither an NPC nor a character fails the load.
Seasons, tiers and sets collapse into one group per boss or prime, so the record says *whom* the NPC
fights alongside, not in which season.

### D4. Index only `unit` and `machineOfWar`

Kind comes from the served `npcs` record. Objects (loot, power-ups) are skipped because they are board
props, not enemies a player looks up. MoW NPCs are kept, although the NPC Library hides them today, so a
future MoW view doesn't need a dataset change.

### D5. Placement in the codebase

- `Models/NpcEncounters.cs`: `GameCatalogNpcEncounter`, `GameCatalogNpcEncounterBattle`, and a
  `NpcEncounterSources` string-constant class (string values, as `LreMissionKinds` does).
- `Denormalization/NpcEncounterDenormalizer.cs` (partial `GameCatalogDenormalizer`): `BuildNpcEncounters`
  taking the finished views plus the two raw readers' output.
- `Validation/NpcEncounterValidation.cs`: the load-fail rules from the spec (`InvalidNpcEncounter`).
- `GameCatalogDatasets.NpcEncounters = "npc-encounters"` added to `Served`. The loader builds it last,
  and it gets a `HashServed` entry and a `GameCatalogSnapshot` field.
- Endpoint: one more `Get("game-catalog/npc-encounters", ...)` in `GetGameCatalogDatasetEndpoints.cs`.

### Contract surface shared with the apps

`GET /api/v1/game-catalog/npc-encounters` and its manifest entry, consumed by
`tacticus-planner-apps` change `add-npc-encounters`, which adds the zod schema, the dataset key (as an
optional dataset, so a client against an older API still syncs) and the NPC Library UI. The `groupId`s
double as calendar join keys on the client: `legendaryEvent` / `questEvent` `groupId` = calendar
`featuredCharacterId`; any `incursion` calendar entry applies to every `incursion` group (an Incursion window opens every MoW; `featuredMowId` only marks a debut).

### Manifest and migrations

The manifest gains one dataset hash and the derived `schemaHash` moves (new payload type). This updates
`GameCatalogSnapshotTests.GameCatalogManifestMatchesSnapshot.verified.txt`. No EF Core migration: no
persisted data changes.

## Risks / Trade-offs

- [Payload size: a common NPC (e.g. Hormagaunt in 21 Onslaught tiers and many campaign battles) has many
  entries] → Entries are small and ids only. The dataset is fetched once per hash like every other.
  Measured on `1.43.157.2989`: 779 records (329 NPCs, 11,660 battle entries), 1.2 MB identity, 44 KB gzip.
- [A datamine bump adds a source shape the readers don't know (new Survival set layout)] → The load
  fails loudly per the spec. The datamine bump PR then has to handle it, which is the existing contract
  for every reader.
- [Strict validation can block a datamine bump on one bad `questUnitId`] → Intended. It matches every
  other dataset's validation. The failure names the unit set.
- [Old content is missing (only current Quest Events, three Survival seasons)] → This is "no recorded
  encounter", not a defect. The spec says so, and the client shows it as such.
- [Onslaught entries over-promise] → The spec says Onslaught means "can appear". The client words it that way.

## Migration Plan

Additive: deploy the API first. Older apps ignore the new dataset. The apps half treats the dataset as
optional until it is served. Rollback is reverting the PR. No stored data depends on it.
