## MODIFIED Requirements

### Requirement: Exact recommendations expose explicit variant rules

Every recommendation SHALL additionally have:

- `id`: a non-empty recommendation id unique across the dataset;
- `mowReplacementIds`: an ordered array of zero or more distinct Machine-of-War ids.

Every hero-slot object (in the `heroSlots` array defined above) SHALL have:

- `heroId`: the exact character id for this slot;
- `roleId`: a non-empty stable role id interpreted by the client;
- `essential`: boolean indicating whether inability to fill this slot makes the recommendation unavailable;
- `replacementCharacterIds`: an ordered array of zero or more distinct character ids explicitly allowed for that slot.

The server SHALL preserve authored recommendation, slot, and replacement
order. Comp core/flex/Machine-of-War lists remain broad advisory guidance and
SHALL NOT be treated as implicit replacements. No investment target, damage
estimate, strategy text, display name, icon path, or localized role label
SHALL be present in the served projection; the recommendation-level
`efficiency` figure defined above is the only effectiveness value served.

#### Scenario: Recommendation supplies deterministic slot rules

- **WHEN** a client reads a recommendation with replacement guidance
- **THEN** it receives a stable id, five slots aligned to the exact heroes, ordered allowed character replacements per slot, and ordered allowed Machine-of-War replacements

#### Scenario: Slot intentionally has no replacement

- **WHEN** an authored hero slot has an empty `replacementCharacterIds` array
- **THEN** the empty list is served unchanged and no Comp member is inferred as a replacement
