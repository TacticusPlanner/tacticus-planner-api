using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>How far ahead of "now" Fixed-recurrence definitions are projected into placeholder slots.</summary>
    public static readonly TimeSpan EventsCalendarProjectionHorizon = TimeSpan.FromDays(15 * 7);

    private const string FixedRecurrenceKind = "Fixed";

    /// <summary><c>event-definitions</c> is served as-is — no transformation beyond the raw->view pass-through.</summary>
    public static IReadOnlyList<GameCatalogEventDefinition> BuildEventDefinitions(
        IReadOnlyList<GameCatalogEventDefinition> definitions) => definitions;

    /// <summary>
    /// Builds the served, date-indexed <c>events-calendar</c>: for every Fixed-recurrence definition,
    /// projects placeholder slots from <paramref name="now"/> through <see cref="EventsCalendarProjectionHorizon"/>
    /// ahead; any authored occurrence whose window overlaps a placeholder's window for the same definition
    /// supersedes it (see add-game-events-calendar-dataset/design.md Decision 3 — overlap, not exact-match).
    /// None-recurrence definitions are never projected. The merged occurrence set is then expanded into a
    /// date-keyed map, repeating multi-day entries under every date they span.
    /// </summary>
    /// <param name="now">
    /// Injected rather than read internally (e.g. from <see cref="DateTimeOffset.UtcNow"/>) so projection is
    /// a pure, deterministic function of its inputs — this is what makes the 15-week boundary directly unit
    /// testable without depending on wall-clock time.
    /// </param>
    public static IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>> BuildEventsCalendar(
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogEventOccurrence> occurrences,
        DateTimeOffset now)
    {
        var occurrencesByDefinition = occurrences
            .GroupBy(occurrence => occurrence.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<GameCatalogEventOccurrence>)group.ToArray(), StringComparer.Ordinal);

        var horizon = now + EventsCalendarProjectionHorizon;
        var entries = new List<GameCatalogEventsCalendarEntry>();

        // Authored occurrences are always confirmed and always included, regardless of the projection window
        // (past/archived occurrences are unbounded — see specs/game-events-calendar-dataset).
        foreach (var occurrence in occurrences)
        {
            entries.Add(new GameCatalogEventsCalendarEntry(
                occurrence.Id, occurrence.DefinitionId, true, occurrence.StartUtc, occurrence.EndUtc, occurrence.Parameters));
        }

        foreach (var definition in definitions)
        {
            var recurrence = definition.Recurrence;
            if (recurrence.Kind != FixedRecurrenceKind
                || recurrence.IntervalDays is not { } intervalDays
                || recurrence.DurationDays is not { } durationDays
                || recurrence.AnchorUtc is not { } anchorUtc)
            {
                // A Fixed-kind definition missing one of its required fields is malformed authored data;
                // rather than throw here, skip projecting it and let ValidateEvents (Validation/EventsValidation.cs)
                // surface a proper RequiredField error instead of a raw null-dereference.
                continue;
            }

            var authored = occurrencesByDefinition.GetValueOrDefault(definition.Id, []);

            foreach (var slotStart in ProjectSlotStarts(anchorUtc, now, horizon, intervalDays))
            {
                var slotEnd = slotStart + TimeSpan.FromDays(durationDays);

                // The earliest yielded slot is the one containing "now" (its start can be up to intervalDays
                // before now); skip it if it has already fully elapsed rather than showing a stale
                // placeholder. Every later slot is necessarily still ahead of now (interval > duration for
                // every current definition — see design.md Risk 1), so this can only ever trim the first one.
                if (slotEnd <= now)
                {
                    continue;
                }

                var superseded = authored.Any(occurrence => occurrence.StartUtc < slotEnd && occurrence.EndUtc > slotStart);
                if (superseded)
                {
                    continue;
                }

                entries.Add(new GameCatalogEventsCalendarEntry(null, definition.Id, false, slotStart, slotEnd, null));
            }
        }

        return ExpandByDate(entries);
    }

    /// <summary>
    /// Every <c>intervalDays</c>-aligned slot start, phase-locked to <paramref name="anchorUtc"/>, whose slot
    /// could possibly still be relevant at or after <paramref name="now"/>, through <paramref name="horizon"/>.
    /// Anchoring to a real reference date (rather than an implicit fixed point like the Unix epoch) is what
    /// makes a weekly modifier land on its intended weekday instead of whatever weekday an arbitrary epoch
    /// happens to fall on.
    /// </summary>
    private static IEnumerable<DateTimeOffset> ProjectSlotStarts(
        DateTimeOffset anchorUtc, DateTimeOffset now, DateTimeOffset horizon, int intervalDays)
    {
        var interval = TimeSpan.FromDays(intervalDays);
        var elapsedIntervals = (long)Math.Floor((now - anchorUtc) / interval);

        // The slot containing "now" (its start is at or before now, by construction of elapsedIntervals) —
        // the caller drops it if it has already fully elapsed. Starting any earlier would only ever
        // reintroduce already-elapsed slots, since interval > duration for every current definition.
        var slotStart = anchorUtc + TimeSpan.FromDays(elapsedIntervals * intervalDays);

        while (slotStart <= horizon)
        {
            yield return slotStart;
            slotStart += interval;
        }
    }

    private static Dictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>> ExpandByDate(
        IReadOnlyList<GameCatalogEventsCalendarEntry> entries)
    {
        var byDate = new Dictionary<string, List<GameCatalogEventsCalendarEntry>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            foreach (var date in EachDate(entry.StartUtc, entry.EndUtc))
            {
                if (!byDate.TryGetValue(date, out var list))
                {
                    list = [];
                    byDate[date] = list;
                }

                list.Add(entry);
            }
        }

        return byDate.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<GameCatalogEventsCalendarEntry>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Every calendar date (UTC, inclusive of start, exclusive of end) an occurrence window spans.</summary>
    private static IEnumerable<string> EachDate(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var day = startUtc.UtcDateTime.Date;
        var lastDay = endUtc.UtcDateTime.AddTicks(-1).Date;

        while (day <= lastDay)
        {
            yield return day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            day = day.AddDays(1);
        }
    }
}
