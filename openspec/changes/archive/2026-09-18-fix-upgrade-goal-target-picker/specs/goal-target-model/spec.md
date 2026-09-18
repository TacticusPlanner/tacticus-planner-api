## ADDED Requirements

### Requirement: Upgrade goal targets accept decomposed base materials

An Upgrade goal target SHALL be accepted when its upgrade id is relevant to the selected
unit, where relevance is the union of:

- the upgrade ids named directly by the unit's own progression — a Character's rank-up
  ladder, or a Machine of War's primary and secondary ability recipes; and
- the base upgrade ids obtained by expanding every crafted upgrade in that set through its
  recipe, recursively, until only non-craftable upgrades remain.

Targets outside that union SHALL be rejected. Widening relevance SHALL NOT invalidate any
previously accepted target.

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
- **THEN** the request is accepted, as before this change

#### Scenario: An unrelated upgrade is still rejected

- **WHEN** a caller targets an upgrade that is neither named by the unit's progression nor
  reachable by expanding that progression's crafted recipes
- **THEN** validation rejects the request as not relevant to the selected unit
