## MODIFIED Requirements

### Requirement: The catalog pins one datamine commit in `REF`

The repository SHALL contain `src/TacticusPlanner.GameCatalog.Compiler/Data/datamine/REF` (the build-time catalog compiler holds the raw data) with exactly one line, LF-terminated, in one of two forms:

```
<tag> <sha>          // tag: "v" + datamine version (e.g. v1.43.157.2989); sha: 40 lowercase hex chars
local <path>         // copies taken from a data-mines working tree; development only
```

The SHA is authoritative. The tag is a label recorded when the pin was made, and SHALL NOT be re-resolved by the build.

#### Scenario: A release pin

- **WHEN** `REF` is `v1.43.157.2989 e5c8c9bc749a7d1bbfb33a3d93056e8826e82678`
- **THEN** `DatamineSourceTests` accepts the pin

#### Scenario: A local pin fails the tests

- **WHEN** `REF` starts with `local`
- **THEN** `DatamineSourceTests` fails with a message saying that a `local` datamine pin cannot be merged, and naming `scripts/pull-datamine.cs` as the fix

#### Scenario: A malformed pin fails the tests

- **WHEN** `REF` has a short SHA, more than one line, or a tag without the `v` prefix
- **THEN** `DatamineSourceTests` fails and names `REF`
