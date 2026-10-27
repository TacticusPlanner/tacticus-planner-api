## MODIFIED Requirements

### Requirement: FarmingLocationIds retains its upgrade-node role

`GoalConfig.farmingLocationIds` SHALL be accepted and returned as the Rank/Ability upgrade-node
farming override. Unlock and Ascension shard sources SHALL be expressed only through
`acquisitionSources`.

#### Scenario: Rank goal farming override is unchanged

- **WHEN** a Rank goal is created with `farmingLocationIds` set
- **THEN** the goal round-trips with the same `farmingLocationIds` and no `acquisitionSources`
  is required

### Requirement: Upgrade goal targets accept decomposed base materials

An Upgrade goal target SHALL be accepted when its upgrade id is relevant to the selected
unit, where relevance is the union of:

- the upgrade ids named directly by the unit's own progression — a Character's rank-up
  ladder, or a Machine of War's primary and secondary ability recipes; and
- the base upgrade ids obtained by expanding every crafted upgrade in that set through its
  recipe, recursively, until only non-craftable upgrades remain. Expansion SHALL track
  visited upgrade ids and SHALL NOT revisit one already expanded, so a recipe cycle
  terminates instead of recursing indefinitely.

Targets outside that union SHALL be rejected.

#### Scenario: A crafted upgrade's base ingredient is accepted

- **GIVEN** a Character whose rank-up ladder names a crafted upgrade, and a base upgrade
  that appears only inside that crafted upgrade's recipe and nowhere on the ladder itself
- **WHEN** a caller creates an Upgrade goal targeting that base upgrade
- **THEN** the request is accepted

#### Scenario: A nested ingredient is accepted

- **GIVEN** a crafted upgrade on the unit's progression whose recipe contains another
  crafted upgrade
- **WHEN** a caller targets a base upgrade reachable only through that nested recipe
- **THEN** the request is accepted

#### Scenario: A literal ladder upgrade remains accepted

- **WHEN** a caller targets a crafted upgrade named directly by the unit's own progression
- **THEN** the request is accepted

#### Scenario: An unrelated upgrade is still rejected

- **WHEN** a caller targets an upgrade that is neither named by the unit's progression nor
  reachable by expanding that progression's crafted recipes
- **THEN** validation rejects the request as not relevant to the selected unit

### Requirement: Progression-derived target caps resolve against the request's effective progression

A target bound that is derived from a unit's progression SHALL be evaluated
against that unit's **effective progression** for the request, not against its
live progression alone. The effective progression SHALL be the higher, on the
progression ladder, of:

- the unit's live progression as recorded in the account's player data, and
- the highest Ascension target present in the same creation request among the
  specs in the goal being validated's full transitive dependency closure (its
  direct dependencies, and their dependencies, and so on) — not only specs it
  directly depends on.

For a request containing a single goal, or a goal that declares no dependency
on an Ascension spec, the effective progression SHALL equal the live
progression.

A unit with no player data recorded SHALL be treated as having no live
progression contribution; the effective progression is then determined solely
by any depended-upon Ascension target in the request.

#### Scenario: Ability target above the live cap is accepted with a depended-upon Ascension

- **GIVEN** a Character whose live progression caps ability levels below the requested level
- **WHEN** one combined request contains an Ascension goal whose target raises the cap to at
  or above the requested level, and an Ability goal declaring a dependency on that Ascension
  spec
- **THEN** the request is accepted and both goals are created

#### Scenario: Ability target above the live cap is rejected without an Ascension

- **GIVEN** a Character whose live progression caps ability levels below the requested level
- **WHEN** a request contains only that Ability goal
- **THEN** the request is rejected, identifying the cap that the target exceeds

#### Scenario: Ability target above even the ascended cap is rejected

- **GIVEN** a Character whose live progression caps ability levels below the requested level
- **WHEN** one combined request contains an Ascension goal whose target still caps ability
  levels below the requested level, and an Ability goal depending on it
- **THEN** the request is rejected, identifying the cap that the target exceeds

#### Scenario: Single-goal creation is unaffected

- **WHEN** an Ability goal is created on its own with a target at or below the cap implied by
  the unit's live progression
- **THEN** creation succeeds

### Requirement: Unlock eligibility counts shop shard offers

A Character SHALL be eligible for an Unlock goal when the Game Catalog gives it at least one
campaign shard-farm node OR at least one regular shard offer in any catalog shop (a shop variant
whose `unitId` is the character and whose reward type is `shards_<unitId>`). A mythic-only shop
offer (`mythicShards_<unitId>`) SHALL NOT make a character eligible, since unlocking consumes
regular shards. A character that is already owned SHALL still be rejected. A character with
neither source SHALL be rejected.

#### Scenario: Shop-only character is accepted

- **GIVEN** a catalog character with no campaign shard nodes and a `shards_<id>` variant in a shop, not owned by the player
- **WHEN** a Character Unlock goal is created for it
- **THEN** the request is accepted

#### Scenario: Mythic-only shop offer is rejected

- **GIVEN** a catalog character with no campaign shard nodes whose only shop variant is `mythicShards_<id>`
- **WHEN** a Character Unlock goal is created for it
- **THEN** the request is rejected as unavailable

#### Scenario: No source is rejected

- **GIVEN** a catalog character with neither campaign shard nodes nor shop shard variants
- **WHEN** a Character Unlock goal is created for it
- **THEN** the request is rejected as unavailable

#### Scenario: Owned shop-only character is rejected

- **GIVEN** a shop-only catalog character the player already owns
- **WHEN** a Character Unlock goal is created for it
- **THEN** the request is rejected as already unlocked

### Requirement: Upgrade goals carry an optional progression range

An Upgrade goal's config SHALL accept, in addition to `targets`, up to three optional range groups:
`rankRange { start, end }`, `activeRange { start, end }` and `passiveRange { start, end }`. Each
group is either fully present or absent. A Character Upgrade goal SHALL accept only `rankRange`; a
Machine of War Upgrade goal SHALL accept only `activeRange` and `passiveRange`, each independently
optional. A group that does not belong to the owning entity type SHALL be rejected. A goal with no
range group remains valid and SHALL be returned with all three groups null. Ranges SHALL NOT affect
which upgrade ids are relevant to the unit (see "Upgrade goal targets accept decomposed base
materials").

#### Scenario: Character Upgrade goal with a rank range

- **WHEN** a caller creates an Upgrade goal for the Character `ultraApothecary` with
  `rankRange { start: Stone2, end: Stone4 }` and a target of `upgArmC002` × 5
- **THEN** the goal is created and its detail returns the same `rankRange`

#### Scenario: MoW Upgrade goal with one ability track

- **WHEN** a caller creates an Upgrade goal for the MoW `astraOrdnanceBattery` with
  `activeRange { start: 1, end: 5 }` and no `passiveRange`
- **THEN** the goal is created, `activeRange` is returned and `passiveRange` is null

#### Scenario: Range group of the wrong unit kind is rejected

- **WHEN** a caller submits `rankRange` for a Machine of War, or `activeRange` for a Character
- **THEN** validation rejects the request

#### Scenario: Invalid range bounds are rejected

- **WHEN** a range has `end <= start`, a rank outside the rank ladder, or an ability level beyond
  the track's ladder
- **THEN** validation rejects the request with a message naming the offending range

#### Scenario: Existing and range-less goals are unchanged

- **GIVEN** an Upgrade goal created without any range group
- **WHEN** its detail is read
- **THEN** all three range groups are null and the goal remains editable

## REMOVED Requirements

### Requirement: Persisted independent equipment goals are removed

**Reason**: L12 — one-off migration ("The migration SHALL delete existing goals…").

**Migration**: None; `UpgradeItem goal type is removed` keeps the live rejection.

### Requirement: The single-select ascension farming source model is removed

**Reason**: L13a — transition requirement describing the old model's removal.

**Migration**: See `goal-target-model` — "FarmingLocationIds retains its upgrade-node role" (shard sources only through `acquisitionSources`).
