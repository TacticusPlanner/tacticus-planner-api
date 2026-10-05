## ADDED Requirements

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

- **GIVEN** an Upgrade goal created before this change, or created without any range group
- **WHEN** its detail is read
- **THEN** all three range groups are null and the goal remains editable

