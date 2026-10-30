## ADDED Requirements

### Requirement: Leaving Archived respects the per-type goal cap

Leaving Archived is the only status transition that grows a profile's count of non-archived goals per type (see `goal-creation`). A `POST /goals/status` that moves Archived goals to Active, Paused or Completed SHALL be a 400 with `issueCode` `goalLimitReached` on `goalIds` when it would put a goal type over 200 non-archived goals. It SHALL change no goal. Transitions that do not leave Archived SHALL NOT be checked against the cap.

#### Scenario: Unarchive at the cap

- **GIVEN** a profile with 200 non-archived Rank goals and an Archived Rank goal A
- **WHEN** it posts `{ goalIds [A], status Active }`
- **THEN** the response is 400 `goalLimitReached` and A stays Archived

#### Scenario: Archiving at the cap

- **GIVEN** a profile with 200 non-archived Rank goals
- **WHEN** it archives one of them
- **THEN** the transition succeeds
