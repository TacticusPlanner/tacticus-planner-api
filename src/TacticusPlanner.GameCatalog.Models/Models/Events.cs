using System.Text.Json;

namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// A definition's recurrence. <c>Kind</c> is either <c>"Fixed"</c> (a known interval and duration —
/// genuinely projectable forward) or <c>"None"</c> (no schedule; the definition only ever appears via an
/// authored <see cref="GameCatalogEventOccurrence"/>, never projected). <c>IntervalDays</c>/<c>DurationDays</c>/
/// <c>AnchorUtc</c> are only present when <c>Kind</c> is <c>"Fixed"</c>. <c>AnchorUtc</c> is the start of a
/// real (past or future) slot that every other projected slot is phase-aligned to — required so slots land
/// on the intended day rather than an arbitrary one (e.g. weekly modifiers must land on their specific
/// weekday, not whatever day an implicit epoch-relative anchor would happen to produce).
/// </summary>
public sealed record GameCatalogEventRecurrence(
    string Kind,
    int? IntervalDays,
    int? DurationDays,
    DateTimeOffset? AnchorUtc);

/// <summary>
/// A reusable event mechanic — served as-is (structural/identity fields only, no display name or icon; the
/// client resolves those from <c>Id</c>). <c>RequiredParameters</c> lists the parameter keys every
/// <see cref="GameCatalogEventOccurrence"/> referencing this definition must supply (enforced at load —
/// see <c>Validation/EventsValidation.cs</c>). <c>Config</c> is mechanic-specific data (scoring, applicable
/// game modes, bonus tiers, ...) passed through opaquely; its shape varies per <c>Type</c> and is not
/// interpreted by the catalog pipeline itself.
/// </summary>
public sealed record GameCatalogEventDefinition(
    string Id,
    string Type,
    GameCatalogEventRecurrence Recurrence,
    IReadOnlyList<string> RequiredParameters,
    JsonElement? Config);

/// <summary>
/// A raw, authored scheduled instance of a definition. Authored occurrences are always confirmed (there is
/// no <c>Confirmed</c> field here — that flag only exists on <see cref="GameCatalogEventsCalendarEntry"/>,
/// where it distinguishes an authored occurrence from a server-projected placeholder).
/// </summary>
public sealed record GameCatalogEventOccurrence(
    string Id,
    string DefinitionId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyDictionary<string, JsonElement>? Parameters);

/// <summary>
/// One self-contained entry in the served, date-indexed <c>events-calendar</c> dataset. <c>OccurrenceId</c>
/// is null for a server-projected placeholder (see <c>Denormalization/EventsDenormalizer.cs</c>) and set for
/// an authored occurrence. An occurrence/placeholder spanning multiple calendar dates appears as an
/// identical entry (same <c>OccurrenceId</c>) under every date it spans.
/// </summary>
public sealed record GameCatalogEventsCalendarEntry(
    string? OccurrenceId,
    string DefinitionId,
    bool Confirmed,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyDictionary<string, JsonElement>? Parameters);
