## Context

`add-project-event-target` added `TargetKind` (enum, `LegendaryEvent` only) and `TargetEventId` on `Project`, validated by `ProjectTargets.Validate` against the catalog's `LreViews`. The web planner now needs season and date targets and an LE run, so it can show whether a project's goals finish before the target's deadline (apps changes `add-project-deadline-targets` and `add-project-deadline-fit`).

## Goals / Non-Goals

**Goals:** store every target kind the planner offers, with strict per-kind validation, without breaking existing clients.

**Non-Goals:** resolving deadlines on the server, checking that a pinned season number exists in the catalog, or adding project-level fit or estimate endpoints.

## Decisions

1. **Flat wire record with per-kind rules.** `ProjectTargetDto(Kind, EventId?, Run?, Season?, Date?)` instead of a polymorphic JSON union. It is one OpenAPI schema, the existing `{ kind, eventId }` payload stays valid, and the validator rejects any field that does not belong to the kind. _Alternative:_ a `oneOf` per kind. Rejected because the generated client types and the hand-written web mapper get harder to keep in sync for little gain.
2. **The server never resolves a deadline.** The deadline instant depends on catalog recurrence data (anchor, interval, season number at anchor) and on LE run dates, which the client already resolves for Season fit and the LE pages. Keeping one resolver, on the client, avoids two paths that can drift. The server validates shape only.
3. **Season numbers are not checked against the catalog.** A pinned season is a positive integer. A past season is allowed, and the client shows it as ended. A season too far ahead simply resolves to a future instant. Catalog season numbering comes from `config.seasonNumberAtAnchor` on `battle-pass`, `guild-raid-season` and `guild-war-season`, which the client reads.
4. **LE `eventId` keeps its catalog check.** The existing rule applies, including the exemption for resending a stored target whose event the catalog dropped. The exemption compares the whole stored target, so changing only `run` on such a project is rejected the same way as a new target.
5. **`customDate` is a `DateOnly`.** A calendar day with no time or zone; the client treats the deadline as 00:00 UTC on the following day. Past dates are allowed.
6. **Migration.** `dotnet ef migrations add AddProjectDeadlineTargets` adds nullable `TargetRun int`, `TargetSeason int` and `TargetDate date` to `Projects`. The existing index on (`ProfileId`, `TargetKind`, `TargetEventId`) is unchanged. No backfill: existing LE rows read as `run` null ("next run").

## Risks / Trade-offs

- A stored pinned season can point at a season the catalog later renumbers. This is accepted; the user can edit the target.
