# Spec Delta

## ADDED Requirements

### Requirement: User settings expose the selected catalog version

The settings response SHALL include `catalogVersion`, either `Latest` or an exact game version string such as `1.43.157.2989`, and `effectiveCatalogVersion`, the game version of the release the player plans with. A new profile, or stored settings without the field, SHALL read `Latest`. A pinned version still in the window is effective as itself. A pin that has left the window SHALL read back unchanged and be effective as the `Latest` release.

#### Scenario: New profile reads Latest

- **WHEN** a profile with no stored settings reads its settings while `1.44.87.3088` is latest
- **THEN** `catalogVersion` is `Latest` and `effectiveCatalogVersion` is `1.44.87.3088`

#### Scenario: Pinned to the previous release

- **WHEN** a profile pinned to `1.43.157.2989` reads its settings while that release is `Previous`
- **THEN** `catalogVersion` and `effectiveCatalogVersion` are both `1.43.157.2989`

#### Scenario: The pin left the window

- **GIVEN** a profile pinned to `1.43.157.2989`
- **WHEN** a deploy rolls the window to `1.45.x` (Latest) and `1.44.87.3088` (Previous) and the profile reads its settings
- **THEN** `catalogVersion` is still `1.43.157.2989` and `effectiveCatalogVersion` is the latest release's game version

### Requirement: The catalog version is saved with user settings

A settings update SHALL accept an optional `catalogVersion`: `Latest` (case-insensitive, returned in PascalCase) or a game version in the window. Any other value SHALL be a `validation` error naming `catalogVersion` that stores nothing. An update without the field SHALL keep the stored value. The existing revision check applies.

#### Scenario: Selecting the previous release

- **WHEN** a profile submits an update with `catalogVersion` equal to `1.43.157.2989` while that release is served, with valid `dailyEnergy` and `xpBookRarity` and the current `revision`
- **THEN** the response and later reads return `1.43.157.2989` and the revision advances by one

#### Scenario: Returning to Latest

- **WHEN** a profile pinned to `1.43.157.2989` submits `catalogVersion` equal to `latest`
- **THEN** the response returns `Latest` and `effectiveCatalogVersion` is the latest release's game version

#### Scenario: Rejecting a version outside the window

- **WHEN** an update carries `catalogVersion` equal to `1.40.47.2500`, or to an empty string
- **THEN** the request is rejected as a bad request with issue code `validation` naming `catalogVersion`, and stored settings and revision are unchanged

#### Scenario: An older client omits the field

- **WHEN** a profile pinned to `1.43.157.2989` submits an update without `catalogVersion`
- **THEN** the pin is kept and the other fields are saved
