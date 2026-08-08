using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class EventsDenormalizerTests
{
    private static GameCatalogEventDefinition FixedDefinition(
        string id, int intervalDays, int durationDays, DateTimeOffset anchorUtc) =>
        new(id, "Test", new GameCatalogEventRecurrence("Fixed", intervalDays, durationDays, anchorUtc), [], null);

    private static GameCatalogEventDefinition NoneDefinition(string id) =>
        new(id, "Test", new GameCatalogEventRecurrence("None", null, null, null), [], null);

    private static GameCatalogEventOccurrence Occurrence(
        string id, string definitionId, DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        new(id, definitionId, startUtc, endUtc, null);

    [Fact]
    public void ProjectsSlotAtExactlyTheHorizonBoundaryButNotPastIt()
    {
        // 15-week horizon = 105 days. intervalDays=105 puts the second slot exactly on the boundary and the
        // third slot 105 days past it.
        var anchor = DateTimeOffset.UnixEpoch;
        var definition = FixedDefinition("le", intervalDays: 105, durationDays: 7, anchorUtc: anchor);
        var now = anchor;

        var calendar = GameCatalogDenormalizer.BuildEventsCalendar([definition], [], now);
        var starts = calendar.Values.SelectMany(entries => entries).Select(entry => entry.StartUtc).Distinct().OrderBy(start => start).ToArray();

        Assert.Contains(anchor, starts); // the slot containing "now" itself
        Assert.Contains(anchor + TimeSpan.FromDays(105), starts); // exactly at the horizon boundary
        Assert.DoesNotContain(anchor + TimeSpan.FromDays(210), starts); // one interval past the boundary
    }

    [Fact]
    public void NoneRecurrenceDefinitionIsNeverProjected()
    {
        var definition = NoneDefinition("ta-power-ups");
        var authored = Occurrence("occ-1", "ta-power-ups", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch + TimeSpan.FromDays(3));

        var calendar = GameCatalogDenormalizer.BuildEventsCalendar([definition], [authored], DateTimeOffset.UnixEpoch);
        var distinctOccurrenceIds = calendar.Values.SelectMany(entries => entries).Select(entry => entry.OccurrenceId).Distinct().ToArray();

        // The authored occurrence itself still appears (confirmed, real data, repeated once per spanned
        // date) — only projection (which would add a null-OccurrenceId placeholder) is skipped.
        Assert.Single(distinctOccurrenceIds, id => id == "occ-1");
        Assert.All(calendar.Values.SelectMany(entries => entries), entry => Assert.True(entry.Confirmed));
    }

    [Fact]
    public void WeeklyModifiersLandOnTheirAnchoredWeekday()
    {
        var sundayAnchor = new DateTimeOffset(2024, 1, 7, 0, 0, 0, TimeSpan.Zero); // a real Sunday
        var saturdayAnchor = new DateTimeOffset(2024, 1, 6, 0, 0, 0, TimeSpan.Zero); // a real Saturday
        var definitions = new[]
        {
            FixedDefinition("always-double-xp-sunday", 7, 1, sundayAnchor),
            FixedDefinition("always-double-gold-saturday", 7, 1, saturdayAnchor),
        };

        // "now" a few months after the anchors, unrelated to their weekday, to prove alignment survives.
        var now = new DateTimeOffset(2026, 3, 12, 15, 30, 0, TimeSpan.Zero);

        var calendar = GameCatalogDenormalizer.BuildEventsCalendar(definitions, [], now);

        foreach (var (date, entries) in calendar)
        {
            var parsedDate = DateOnly.ParseExact(date, "yyyy-MM-dd");
            foreach (var entry in entries)
            {
                var expectedWeekday = entry.DefinitionId == "always-double-xp-sunday" ? DayOfWeek.Sunday : DayOfWeek.Saturday;
                Assert.Equal(expectedWeekday, parsedDate.DayOfWeek);
            }
        }

        Assert.Contains(calendar.Values.SelectMany(entries => entries), entry => entry.DefinitionId == "always-double-xp-sunday");
        Assert.Contains(calendar.Values.SelectMany(entries => entries), entry => entry.DefinitionId == "always-double-gold-saturday");
    }

    [Fact]
    public void AuthoredOccurrenceOverlappingAPlaceholderSupersedesIt()
    {
        var anchor = DateTimeOffset.UnixEpoch;
        var definition = FixedDefinition("le", intervalDays: 35, durationDays: 7, anchorUtc: anchor);
        // Authored occurrence's dates drift slightly from the raw-cadence placeholder it replaces (2 days late).
        var authored = Occurrence("occ-le-1", "le", anchor + TimeSpan.FromDays(2), anchor + TimeSpan.FromDays(9));

        var calendar = GameCatalogDenormalizer.BuildEventsCalendar([definition], [authored], anchor);
        var allLeEntries = calendar.Values.SelectMany(entries => entries).Where(entry => entry.DefinitionId == "le").ToArray();

        // Only the placeholder overlapping the authored occurrence's own window is superseded — later,
        // non-overlapping slots within the 15-week horizon are untouched and still legitimately projected.
        var entriesOverlappingAuthoredWindow = allLeEntries
            .Where(entry => entry.StartUtc < authored.EndUtc && entry.EndUtc > authored.StartUtc)
            .ToArray();
        Assert.All(entriesOverlappingAuthoredWindow, entry => Assert.Equal("occ-le-1", entry.OccurrenceId));
        Assert.All(entriesOverlappingAuthoredWindow, entry => Assert.True(entry.Confirmed));
        Assert.Contains(allLeEntries, entry => entry.OccurrenceId is null); // later slots still projected
    }

    [Fact]
    public void AuthoredOccurrenceWithNoOverlappingPlaceholderStillAppears()
    {
        var definition = NoneDefinition("hse-warp-surge");
        var start = new DateTimeOffset(2026, 5, 22, 0, 0, 0, TimeSpan.Zero);
        var authored = Occurrence("occ-warp-1", "hse-warp-surge", start, start + TimeSpan.FromDays(3));

        var calendar = GameCatalogDenormalizer.BuildEventsCalendar([definition], [authored], start);
        var distinctOccurrenceIds = calendar.Values.SelectMany(entries => entries).Select(entry => entry.OccurrenceId).Distinct().ToArray();

        Assert.Single(distinctOccurrenceIds, id => id == "occ-warp-1");
    }

    [Fact]
    public void SingleDayEntryAppearsUnderExactlyOneDate()
    {
        var start = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        var occurrence = Occurrence("occ-1", "game-version-release", start, start + TimeSpan.FromDays(1));

        var calendar = GameCatalogDenormalizer.BuildEventsCalendar([NoneDefinition("game-version-release")], [occurrence], start);

        var datesContainingEntry = calendar.Where(pair => pair.Value.Any(entry => entry.OccurrenceId == "occ-1")).Select(pair => pair.Key).ToArray();
        string[] expectedDates = ["2026-09-02"];
        Assert.Equal(expectedDates, datesContainingEntry);
    }

    [Fact]
    public void MultiDayEntrySpansEveryDateWithTheSameOccurrenceId()
    {
        var start = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
        var end = start + TimeSpan.FromDays(7); // 26 Jul -> 2 Aug, exclusive end
        var occurrence = Occurrence("occ-lucius", "legendary-event", start, end);

        var calendar = GameCatalogDenormalizer.BuildEventsCalendar([NoneDefinition("legendary-event")], [occurrence], start);

        var datesContainingEntry = calendar.Where(pair => pair.Value.Any(entry => entry.OccurrenceId == "occ-lucius")).Select(pair => pair.Key).OrderBy(date => date).ToArray();
        string[] expectedDates = ["2026-07-26", "2026-07-27", "2026-07-28", "2026-07-29", "2026-07-30", "2026-07-31", "2026-08-01"];
        Assert.Equal(expectedDates, datesContainingEntry);
    }
}
