## MODIFIED Requirements

### Requirement: `scripts/pull-datamine.cs` refreshes the pin and copies

`dotnet run scripts/pull-datamine.cs -- <tag-or-sha>` SHALL resolve the argument to a commit of `TacticusPlanner/tacticus-planner-data-mines` with the caller's git credentials, copy the allowlisted paths at that commit byte for byte, delete unlisted copies, rewrite `files.sha256`, and write `REF` (a SHA argument records its release tag, or `untagged`). It SHALL print the upstream `raw/VERSION` and each path's status (added, changed, removed, unchanged).

#### Scenario: Local working tree

- **WHEN** a maintainer runs the script with `--source ../tacticus-planner-data-mines`
- **THEN** it copies from that working tree and writes `REF` as `local ../tacticus-planner-data-mines`

#### Scenario: Bump to a new release

- **WHEN** a maintainer runs `dotnet run scripts/pull-datamine.cs -- v1.44.0.3000` and `items.json` changed upstream
- **THEN** `REF` holds `v1.44.0.3000` and that tag's SHA, the `items.json` copy and its checksum line are updated, unchanged copies are reported unchanged, and `DatamineSourceTests` passes

#### Scenario: A path leaves the allowlist

- **WHEN** a path is removed from `files.txt` and the script is re-run at the same pin
- **THEN** its copy and checksum line are deleted, it is reported removed, and `REF` is unchanged
