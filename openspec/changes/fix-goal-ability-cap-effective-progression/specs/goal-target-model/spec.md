## ADDED Requirements

### Requirement: Progression-derived target caps resolve against the request's effective progression

A target bound that is derived from a unit's progression SHALL be evaluated
against that unit's **effective progression** for the request, not against its
live progression alone. The effective progression SHALL be the higher, on the
progression ladder, of:

- the unit's live progression as recorded in the account's player data, and
- the highest Ascension target present in the same creation request among the
  specs that the goal being validated declares a dependency on.

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
- **THEN** creation succeeds, unchanged from prior behavior

### Requirement: The raised cap requires a declared dependency

A goal SHALL obtain a progression-derived cap above its live one only from
Ascension specs it declares a dependency on within the same request. An
Ascension spec present in the request but not depended upon SHALL NOT raise
another spec's cap.

#### Scenario: Undeclared Ascension does not raise the cap

- **GIVEN** a Character whose live progression caps ability levels below the requested level
- **WHEN** one combined request contains a qualifying Ascension goal and an Ability goal that
  declares no dependency on it
- **THEN** the request is rejected, identifying the cap that the target exceeds

#### Scenario: Dependency on a non-Ascension spec does not raise the cap

- **GIVEN** a Character whose live progression caps ability levels below the requested level
- **WHEN** an Ability goal declares a dependency only on an Unlock spec in the same request
- **THEN** the request is rejected, identifying the cap that the target exceeds
