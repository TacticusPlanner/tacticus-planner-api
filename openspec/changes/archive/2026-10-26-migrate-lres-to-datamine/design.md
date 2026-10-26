## Context

The raw `GameCatalogLre` record (and everything built from it: views, battles, validation) stays. Only its
source changes: `LreDatamineReader.ReadDatamineLres` builds it from datamine files plus one curated file.

## Decisions

1. **Curated keys pick the events.** The game lists 14 legendary events, including finished ones. The
   catalog serves the four it served before; `lre-curation.json` names each by its datamine `event`, and the
   reader checks that the event unlocks that unit. Dataset keys stay `lres-{unit in lower case}`.
2. **What stays curated.** Battle completion points (`battlesPoints`) are not in the datamine. Track names,
   enemy labels and links and restriction names are hand-written display text with irregular wording
   ("No Terminator", "Orks "). `allowedUnitsFilter` is curated too, since V1's filters are hand-picked
   (Farsight's gamma also excludes Orks). Run starts are announced, not configured.
3. **Missions keep V1's English lines.** `LreTaskText` ports V1's `questsToMissionText`, with faction and
   trait labels from the reversed parser alias tables, and the parser reads them back. A task kind or label
   the reader does not know fails the load, as an unknown line did before.
4. **Restrictions** are the objectives after each battle's leading `Acing` objective and must be identical in
   every battle of a track. `NotDamageType` and `NotTrait` serve as excluding filters; `HasRangedAttack` and
   `HasNoRangedAttack` serve as V1's `AttackType` `Ranged` filter. The `Acing` score is `defeatAll`, and the
   first battle's is `killPoints`.
5. **Rewards.** Points milestones are the event's `rewardProgressId` ladder (each step must pay event currency).
   Chests are the products of its shop segment in order; every chest must grant the same event shard amount,
   which is `shardsPerChest`. Progression is the event-shard ascension cost of Legendary 8 to 11 stars and
   Mythic 11 and 12 stars, in served field order.
6. **Large files** (`battleSets.json`, `chests.json`, `products.json`) are bound as raw elements, and only the
   entries an event names are deserialized. `groups.json` is now loaded once and shared with mythic-quests.

## Risks

- Mission indices move for Uthar's premium list and Farsight's last two premium missions. V2 is pre-production
  and the game is the source of truth, so no data migration is added.
