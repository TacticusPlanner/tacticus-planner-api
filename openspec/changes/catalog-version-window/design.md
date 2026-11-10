# Design

## Context

See proposal.md (Why). The current state:

- **Loading.** `GameCatalogLoader.Load(asOf)` (`src/TacticusPlanner.GameCatalog/GameCatalogLoader.cs`) is static. It reads datamine copies through `DatamineFiles`, which has the fixed resource prefix `datamine/`. It builds one `GameCatalogSnapshot` and validates it.
- **Release metadata.** `GameCatalogRelease` is static per assembly: `GameVersion` comes from the embedded `datamine/VERSION`, and `Version` is `GameVersion+sha256[..12]` over every embedded resource.
- **Holding.** `EmbeddedGameCatalogProvider` is a DI singleton. It exposes only `Current` and rebuilds it under a lock when the UTC day changes, because `asOf` drives the events calendar.
- **Serving.** `ServedDatasets` caches bodies, hashes and manifests per snapshot in `ConditionalWeakTable`s. `ServedDatasetEndpoint<T>` returns 404 for an envelope hash that isn't the current one.
- **Consumers.** Every consumer reads `catalog.Current`: goal target validation, Legendary Event plans and run records, projects, battle-progress overrides, player-data sync, guild raid status, roster snapshots, and V1 imports.
- **Settings.** `UserSettingsData` is stored as a JSON column (`user_settings.settings`).

The 1.43.157.2989 → 1.44.87.3088 bump (api#194) is the reference case: 25 of 44 datamine files changed, 16 of 31 served dataset hashes moved, the code did not change at all, and 14 tests that pin game facts did.

Companion change: `catalog-version-window` in `tacticus-planner-apps`. The shared contract is the versions endpoint, the manifest `gameVersion` parameter, content-addressed envelope URLs, and the `catalogVersion` / `effectiveCatalogVersion` settings fields.

## Goals / Non-Goals

**Goals:**

- Two immutable snapshots, built by today's loader, one per release.
- Additive contract changes only. The deployed apps build keeps getting `Latest` everywhere.
- Server-side validation never rejects a client's id because of which release that client selected.

**Non-Goals:**

- **More than two releases, or a configurable window.** The window is fixed at two.
- **Reproducing old calculations.** Rows keep recording their catalog version, but nothing reloads a release that has left the window.
- **Per-release curated data.** Curated files stay shared.
- **Schema negotiation between client and API.** This is ADR 0004's deferred gap. Both releases are served by the same code, so they share one `schemaHash`, which keeps this change inside that gap rather than widening it.

## Decisions

### D1. Two release folders with their own pins, not a second repository or download at startup

The folders are `Data/datamine/latest` and `Data/datamine/previous`, embedded as `datamine/<release>/<raw path>`, with one shared `files.txt`. `DatamineFiles` becomes an instance bound to a release.

- **Why:** it keeps the build hermetic and keeps the existing checksum gate (`DatamineSourceTests`), now run per release. About 21 MB of raw JSON per release in the assembly is acceptable.
- **Alternative:** fetch the previous release from data-mines at startup. Rejected: it adds a startup network dependency on a private repository.

### D2. One shared loader that runs once per release

- `GameCatalogLoader.Load(asOf)` becomes `Load(release, asOf)`.
- `GameCatalogRelease` stops being static: each snapshot carries its own `GameVersion`/`Version`, computed over its release's datamine resources plus the curated resources.
- The provider exposes `Latest`, `Previous`, `Get(gameVersion)` and `Window`. `Current` stays as a forwarding alias of `Latest`, so untouched consumers keep compiling and behaving the same.
- The daily `asOf` rebuild applies to both snapshots.
- **Alternative:** a separate loader per release. Rejected: two code paths for the same denormalization.
- **Risk:** the previous release must still load under the current code. See Risks.

### D3. Release selection is a query parameter on the existing routes; the versions list is a new endpoint

- `manifest?gameVersion=` without the parameter is unchanged.
- Content-addressed URLs are already unique per envelope, so the dataset endpoint looks a hash up across both snapshots' envelope indexes. They are already cached per snapshot in `ServedDatasets`, so no new routes are needed.
- `GET game-catalog/versions` is small, anonymous and has an ETag.
- **Alternative:** release-scoped route prefixes such as `/game-catalog/v/{gameVersion}/...`. Rejected: it duplicates 31 endpoint registrations for no caching gain, since the URLs are already content-addressed.

### D4. Validation accepts an id from either release

Validators take a "known in the window" lookup instead of `Current`. It is built once per snapshot pair as the union of each id set, so lookup cost is unchanged.

- **Why:** a player switches back and forth during rollout. Strict checking against the selected release would break saving a goal they created while on the other release, and the client keeps local progress for both (see the apps design).
- **Alternative:** strict per-version validation. Rejected for the reason above.

The paths split as follows:

- **Union:** `GoalTargetValidationService` and the `GameCatalogGoalLookups` it uses; `LegendaryEventCatalogValidator`; `LegendaryEventPlanEndpointBase`; `ProjectTargets`; `BattleProgressEndpoints`; `CampaignEventProgressEndpoints`; `IncursionProgressEndpoints`.
- **Effective release:** projections that compute from catalog data for one player (`LegendaryEventPlanProjection`, `LegendaryEventRunRecordRecorder`) read the snapshot of the player's effective version.
- **Latest always:** `PlayerDataTransformer`, `GuildRaidStatusService`/`Projector`, and V1 imports.
  - V1 imports keep validating against `Latest` plus `Previous`. They record `v1-import` as today.

### D5. The setting is a string in `UserSettingsData`, resolved on read

- `CatalogVersion` (string) holds `null` for `Latest`, or a game version.
- **No migration:** it's a JSON column, and a missing field deserializes to `null`.
- `GET settings` returns `catalogVersion` as `"Latest"` or the stored version, plus `effectiveCatalogVersion`, resolved against the current window at read time. A stale pin is **not** rewritten on read, so a GET never writes.
- `PUT settings` accepts `catalogVersion?`: absent keeps the stored value; `latest` (case-insensitive) clears it; a window version pins it; anything else returns `400` with issue code `validation` and an `errors` entry for `catalogVersion`.
- An `IEffectiveCatalog` service resolves the snapshot for the authenticated profile. It reads the settings row and is cached per request. LE plan, run record and roster snapshot writers use it to stamp `Version`.
- **Alternative:** a relative `Previous` value. Rejected: a relative value silently changes what a player plans with when a release rolls in.

### D6. `pull-datamine.cs` gains roll and refresh modes

- **Roll** (the default with a tag): it validates the target first, then renames `latest` to `previous`, then fills `latest`. The order matters: nothing is written unless every listed path exists, keeping the "changes nothing" guarantee across both folders.
- **`--refresh <release>`:** re-pulls in place. Use it after a forced datamine re-run that moved a tag, or when the allowlist changes.

## Risks / Trade-offs

- **[The previous release fails under new code.]** A loader change made for a new release, for example a new reward kind, could reject the old data.
  - **Mitigation:** release-agnostic tests (both snapshots load and validate; envelope lookup across them).
  - Tests that pin game facts stay pinned to `Latest` only. The 1.44 bump changed 14 such tests; doubling them per release would double that maintenance.
  - If the previous release really cannot load, the bump has to fix the reader for both, which is the same work as today, just surfaced earlier.
- **[Memory and startup time roughly double.]** Today's notes put served bodies at about 0.35 MB compressed and identity bodies at about 5 MB per snapshot.
  - **Mitigation:** measure startup and working set before and after in the apply step. The container's memory limit is unchanged unless that measurement says otherwise. Infra is not part of this change.
- **[Curated data is shared.]** A curated correction made for the latest release also applies to the previous one. This is acceptable: curated data is mostly drop chances and quest metadata that rarely differ between adjacent releases.
- **[Union validation accepts an id the selected release lacks.]** For example, a goal for Daras while on 1.43. The client must render such ids gracefully; that's covered in the apps change. Server calculations on the effective release skip ids missing from it rather than failing.
- **[Two dataset envelopes share a `hash` but have different URLs.]** The client must key its caches by URL or envelope, not by dataset `hash` alone. The apps change uses a database per game version, so this cannot collide.

## Migration Plan

1. **First deploy:**
   - Move today's `Data/datamine/{raw,REF,files.sha256}` to `latest/` (1.44.87.3088 after api#194 merges).
   - Pull `v1.43.157.2989` into `previous/` with `--refresh previous`.
   - The served `Latest` is unchanged for every existing client.
2. **Rollback:** a revert restores the single folder. Settings rows with a stored `catalogVersion` keep the unknown JSON field harmlessly.
3. **Every later datamine bump:** `pull-datamine -- <tag>` rolls the window. The workspace `tp-bump-datamine` skill is updated to say so.

## Open Questions

- Whether to expose a release date in `versions` for the client's banner copy. Data-mines release timestamps would need embedding next to `REF`. The apps change does not need it; it compares game versions.
