## ADDED Requirements

### Requirement: Gaining a usable Tacticus integration is reported

The system SHALL report a `tacticus_integration_configured` event when a profile gains a usable Tacticus API key. The event SHALL carry which path supplied the key and whether the profile had no usable key beforehand, so that first-time activation is distinguishable from a later key replacement. A request that does not result in a stored, validated key SHALL NOT report the event.

#### Scenario: A first key is accepted

- **WHEN** a profile with no usable Tacticus API key supplies one and it is validated and stored
- **THEN** a `tacticus_integration_configured` event is reported, attributed to that profile's analytics id, carrying the supplying path and indicating that this was the profile's first usable key

#### Scenario: A key arrives through a V1 profile import

- **WHEN** a profile with no usable Tacticus API key gains one as part of a V1 profile import
- **THEN** a `tacticus_integration_configured` event is reported, carrying the import path rather than the direct path

#### Scenario: An existing key is replaced

- **WHEN** a profile that already has a usable Tacticus API key supplies a different one and it is validated and stored
- **THEN** a `tacticus_integration_configured` event is reported, indicating that this was not the profile's first usable key

#### Scenario: The key is rejected

- **WHEN** a supplied Tacticus API key fails validation and is not stored
- **THEN** no `tacticus_integration_configured` event is reported

#### Scenario: Only the Tacticus user id changes

- **WHEN** a request updates or clears the Tacticus user id without supplying a new API key
- **THEN** no `tacticus_integration_configured` event is reported

### Requirement: A completed V1 profile import is reported with per-part outcomes

The system SHALL report a `v1_import_completed` event when a V1 profile import request completes. The event SHALL carry a separate outcome for each importable part — whether that part was imported, skipped, or failed — rather than a single overall outcome, so that the parts that fail most often are identifiable. It SHALL also carry the number of goals parsed and the number of goals skipped, each reported as a bucket rather than an exact count.

#### Scenario: An import completes with mixed part outcomes

- **WHEN** a V1 profile import completes in which some parts are imported, some are not selected, and some fail
- **THEN** a single `v1_import_completed` event is reported carrying each part's own outcome

#### Scenario: Goal volume is bucketed

- **WHEN** a V1 profile import parses and skips goals
- **THEN** the reported event carries the parsed and skipped goal volumes as buckets, and does not carry exact counts

#### Scenario: The import request is rejected outright

- **WHEN** a V1 profile import request fails before any part is attempted
- **THEN** no `v1_import_completed` event is reported

#### Scenario: Import details are not reported

- **WHEN** any `v1_import_completed` event is reported
- **THEN** it carries no player name, guild name, API key or key fragment, Tacticus user id, goal name, or free-text issue message

### Requirement: The outcome of a player data sync is reported

The system SHALL report a `player_sync_completed` event when a player data sync attempt concludes, for both successful and failed attempts. The event SHALL carry the outcome, an enumerated failure reason when the attempt failed, and whether the profile had no prior player data snapshot at the time of the attempt. The failure reason SHALL be drawn from a fixed set of categories and SHALL NOT be an upstream message or response body.

#### Scenario: A first successful sync

- **WHEN** a profile with no prior player data snapshot completes a sync successfully
- **THEN** a `player_sync_completed` event is reported with a successful outcome, indicating that this was the profile's first sync

#### Scenario: A later successful sync

- **WHEN** a profile that already has a player data snapshot completes a sync successfully
- **THEN** a `player_sync_completed` event is reported with a successful outcome, indicating that this was not the profile's first sync

#### Scenario: A sync that changes nothing still reports

- **WHEN** a sync succeeds but no stored content changed
- **THEN** a `player_sync_completed` event is reported with a successful outcome

#### Scenario: The upstream game API rejects the attempt

- **WHEN** a sync attempt fails because the configured key is rejected, the upstream is rate limited, or the upstream errors
- **THEN** a `player_sync_completed` event is reported with a failed outcome and the corresponding enumerated failure reason

#### Scenario: Failure detail carries no upstream content

- **WHEN** a `player_sync_completed` event is reported for a failed attempt
- **THEN** its failure reason is one of the fixed categories, and no upstream response body, exception message, or key fragment is carried

### Requirement: Goal creation is reported per request

The system SHALL report a `goals_created` event once per successful goal creation request, not once per created goal. The event SHALL carry whether the request created a single goal or a combined set, and the number of goals created as a bucket rather than an exact count. A rejected or failed creation request SHALL NOT report the event.

#### Scenario: A single goal is created

- **WHEN** a caller successfully creates one goal
- **THEN** one `goals_created` event is reported, indicating a single-goal creation

#### Scenario: A combined set is created

- **WHEN** a caller successfully creates a combined set of goals in one request
- **THEN** exactly one `goals_created` event is reported for the request, indicating a combined creation and carrying the created volume as a bucket

#### Scenario: Creation is rejected

- **WHEN** a goal creation request fails validation or conflict detection and no goal is created
- **THEN** no `goals_created` event is reported

### Requirement: Goal creation reports the shape of what was created

The `goals_created` event SHALL describe the shape of the created goals using the system's own closed vocabularies, so that which kinds of planning the product actually carries is measurable without inspecting stored goals. It SHALL carry:

- the category of unit targeted;
- the set of distinct goal kinds created by the request;
- the set of distinct farming strategies chosen;
- whether the goals were assigned to explicitly chosen projects or fell back to the caller's default project;
- whether the created goals entered the caller's active plan or were parked.

Every value reported SHALL be drawn from a fixed, system-owned set of values. Where a request creates goals of several kinds or strategies, the reported sets SHALL be distinct and order-independent, so that the same combination is always reported identically.

#### Scenario: A single goal's shape is reported

- **WHEN** a caller successfully creates one goal
- **THEN** the reported event carries the targeted unit category, a single goal kind, a single farming strategy, the project assignment, and whether the goal became active or parked

#### Scenario: A combined request of several kinds

- **WHEN** a caller successfully creates a combined set containing several goal kinds in one request
- **THEN** the reported event carries the distinct set of kinds created, rather than only the first or only one event per kind

#### Scenario: The same combination reported twice

- **WHEN** two requests create the same combination of goal kinds in a different order
- **THEN** both report the same set value

#### Scenario: Default project fallback

- **WHEN** a caller creates a goal without naming any project, so it falls back to their default project
- **THEN** the reported event indicates the default-project fallback rather than an explicit project choice

### Requirement: Goal creation reports which optional planning features were used

The `goals_created` event SHALL report whether the request used each optional goal-planning feature, so that a feature's adoption is measurable independently of overall goal volume. It SHALL carry:

- whether shard acquisition sources were explicitly selected, distinguished from the unrestricted default;
- the set of distinct acquisition source kinds selected, when any were;
- whether a per-goal farming location override was supplied;
- whether the request declared dependencies between the goals it created.

Acquisition source kinds SHALL be reported as kinds only. The specific battles, shop offers, or locations chosen SHALL NOT be reported.

#### Scenario: Sources are explicitly selected

- **WHEN** a caller creates a goal for which they explicitly select shard acquisition sources
- **THEN** the reported event indicates that selection was used and carries the distinct kinds selected

#### Scenario: The unrestricted default is left in place

- **WHEN** a caller creates a goal that could take acquisition sources but leaves the unrestricted default
- **THEN** the reported event distinguishes this from an explicit selection, and reports no source kinds

#### Scenario: The goal kind takes no sources at all

- **WHEN** a caller creates a goal of a kind for which acquisition sources do not apply
- **THEN** the reported event is distinguishable from both an explicit selection and a left-in-place default

#### Scenario: Chosen locations are not reported

- **WHEN** a caller selects specific campaign battles, shop offers, or a farming location override
- **THEN** the reported event indicates only that the feature was used and, for acquisition sources, the kinds — never the chosen battle, offer, or location identifiers

#### Scenario: Dependencies between combined goals

- **WHEN** a caller creates a combined set in which some goals depend on earlier ones
- **THEN** the reported event indicates that dependencies were declared

### Requirement: Goal targets and identities are never reported

A `goals_created` event SHALL NOT carry the identity of the unit a goal targets, the goal's target values, the identifiers of the goals or projects created, or any user-authored text. Questions about which units are planned for, and to what targets, SHALL be answered from stored data rather than by reporting them as event properties.

#### Scenario: Goal content is not reported

- **WHEN** any `goals_created` event is reported
- **THEN** it carries no unit identity, target rank, progression, ability, level or upgrade value, goal identifier, project identifier, goal name, or note

#### Scenario: A request with rich targets

- **WHEN** a caller creates a goal specifying detailed start and end targets
- **THEN** the reported event describes only the goal's kind and the optional features used, and carries none of the target values

### Requirement: Project activation is reported

The system SHALL report a `project_activated` event when a project is successfully made the active project for a profile. A request that fails, or that activates the project that is already active, SHALL NOT report the event.

#### Scenario: A project becomes active

- **WHEN** a caller successfully activates a project that was not already the active project
- **THEN** a `project_activated` event is reported, attributed to that caller's analytics id

#### Scenario: Re-activating the already-active project

- **WHEN** a caller activates the project that is already active for their profile
- **THEN** no `project_activated` event is reported

#### Scenario: Activation fails

- **WHEN** a project activation request fails validation or authorization
- **THEN** no `project_activated` event is reported

#### Scenario: Project identity is not reported

- **WHEN** a `project_activated` event is reported
- **THEN** it carries no project identifier, project name, or other user-authored value

### Requirement: Numeric properties are reported as buckets

Where a reported event carries a quantity derived from a user's own data — a roster size, a goal count, an import volume — it SHALL report that quantity as a bucket drawn from a fixed set of ranges, and SHALL NOT report the exact value. Buckets SHALL be coarse enough that an unusually large value does not distinguish a single account.

#### Scenario: A quantity is reported

- **WHEN** an event carries a quantity derived from a user's own data
- **THEN** the reported value is a bucket label from a fixed set, not the exact number

#### Scenario: An unusually large quantity

- **WHEN** a user's data produces a quantity far above typical
- **THEN** it is reported in the highest bucket rather than as a distinguishing exact value
