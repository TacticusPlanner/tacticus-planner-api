## ADDED Requirements

### Requirement: Account purge removes guild-visible copies

`DELETE account` SHALL clear the guild-visible copies derived from the profile, in the same transaction that deletes the profile and every row it owns. These copies are the linked player name and the last-active-in-Planner time on the profile's guild-member row, and the member's profile link. Guildmates SHALL stop seeing them at once, without waiting for a guild sync. The guild's stored token is not affected.

#### Scenario: Guildmate no longer sees the purged name

- **GIVEN** profile A is linked to a guild member that a guildmate sees as "A's name"
- **WHEN** A sends `DELETE account`
- **THEN** the guildmate's next `GET guild` shows that member with no linked player name and no last-active time

### Requirement: Account purge reports whether the sign-in account was deleted

The 204 from `DELETE account` SHALL carry the `X-Sign-In-Account-Deleted` header:

- `true` only when the best-effort Microsoft Entra deletion actually removed the sign-in account;
- `false` when it was not requested, or when it did not happen.

The header SHALL be exposed to allowed cross-origin callers. The request SHALL stay idempotent.

#### Scenario: Sign-in deletion not requested

- **WHEN** the caller sends `DELETE account` without `deleteSignInAccount`
- **THEN** the response is 204 with `X-Sign-In-Account-Deleted: false`

#### Scenario: Sign-in deletion fails

- **GIVEN** the Entra deletion cannot be performed
- **WHEN** the caller sends `DELETE account?deleteSignInAccount=true`
- **THEN** the application data is deleted and the response is 204 with `X-Sign-In-Account-Deleted: false`
