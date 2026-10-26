# goal-creation Specification

## Purpose
Defines what a goal creation request may set on each new goal beyond its target, kind and dependencies.

## Requirements

### Requirement: Creation may set each goal's notes

Each goal in a `POST /goals` request MAY carry `notes` (string or null). Notes SHALL follow the same rule as the edit route's `details.notes`: at most 2000 characters, stored as sent, null meaning no notes. A goal's notes SHALL be saved on that goal only and returned in its detail. Notes over the limit SHALL be a 400 validation problem naming that goal's `notes`, and nothing is created.

#### Scenario: Notes on one goal of two

- **WHEN** a request creates a Rank and an Ascension goal with notes "For Guild Raid" on the Rank goal only
- **THEN** the Rank goal's detail has that note and the Ascension goal's notes are null

#### Scenario: Notes too long

- **WHEN** a goal in the request carries 2001 characters of notes
- **THEN** the response is 400 `validation` naming that goal's notes, and no goal is created
