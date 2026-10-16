# Spec Delta

## MODIFIED Requirements

### Requirement: One plan per profile and Legendary Event

The system SHALL persist at most one plan per profile and catalog Legendary Event id. A plan SHALL carry a monotonic `revision`, the catalog version it was last written under (`catalogVersion`), optional `notes` (at most 2000 characters) and a `showPaidOptions` flag (default false). Plans SHALL be scoped to the caller's profile: a plan is never readable or writable by another profile, and deleting the account removes its plans and everything beneath them. A stored plan SHALL stay readable after its event leaves the current catalog; only writes require a current catalog event.

#### Scenario: A missing plan reads as an empty plan

- **GIVEN** a profile with no plan for event `astarLysander`
- **WHEN** the caller requests `GET /me/legendary-event-plans/astarLysander`
- **THEN** the response is 200 with `eventId` `astarLysander`, `revision` 0, `notes` null, `showPaidOptions` false, an empty `teams` list and the current `catalogVersion`, and no row is created

#### Scenario: Unknown event id

- **GIVEN** the profile has no stored plan for `notAnEvent`, which is not a current catalog `lres` id
- **WHEN** the caller requests or writes `/me/legendary-event-plans/notAnEvent`
- **THEN** the response is 404

#### Scenario: A stored plan outlives its catalog event

- **GIVEN** the profile has a plan at revision 4 for event `x`, and a later catalog no longer serves `x`
- **WHEN** the caller requests `GET /me/legendary-event-plans/x`
- **THEN** the response is 200 with the stored plan at revision 4 and its stored `catalogVersion`

#### Scenario: Writes to a plan outside the catalog are rejected

- **GIVEN** the same plan for `x`, no longer in the catalog
- **WHEN** the caller sends any `PUT`, `POST` or `DELETE` under `/me/legendary-event-plans/x`
- **THEN** the response is 404 and the plan is unchanged

#### Scenario: Plan-level write creates the plan

- **GIVEN** no plan exists for the event
- **WHEN** the caller sends `PUT /me/legendary-event-plans/{eventId}` with `expectedRevision` 0, `notes` "Alpha first" and `showPaidOptions` true
- **THEN** the plan is created, the response is 200 with `revision` 1, the given fields and the current `catalogVersion`

#### Scenario: Account purge cascades

- **GIVEN** a profile with a plan that has teams, members and objectives
- **WHEN** the account is deleted
- **THEN** the plan and all of its teams, members and objectives are removed

## ADDED Requirements

### Requirement: Every stored plan is listed in one read

`GET /me/legendary-event-plans` SHALL return `LegendaryEventPlanListResponse { plans: LegendaryEventPlanResponse[] }` with every plan stored for the caller's profile, in the served plan shape, ordered by `eventId` (ordinal). Only stored plans SHALL be listed: a catalog event the caller never wrote a plan for SHALL NOT appear as an empty plan. Plans whose event is no longer in the catalog SHALL be listed with their stored `catalogVersion`. The read SHALL create nothing and SHALL NOT change any revision.

#### Scenario: Two stored plans

- **GIVEN** the caller has plans for `votanUthar` (revision 6, two Alpha teams) and `astarLysander` (revision 2, no teams, notes "Alpha first")
- **WHEN** the caller requests `GET /me/legendary-event-plans`
- **THEN** the response is 200 with `plans` `[astarLysander, votanUthar]`, each exactly as its single `GET` returns it

#### Scenario: No plans

- **GIVEN** the caller has never written a plan
- **WHEN** the list is requested
- **THEN** the response is 200 with `plans` `[]`

#### Scenario: Another profile's plans are not listed

- **GIVEN** profiles A and B each have a plan for `votanUthar`
- **WHEN** A requests the list
- **THEN** only A's plan is returned
