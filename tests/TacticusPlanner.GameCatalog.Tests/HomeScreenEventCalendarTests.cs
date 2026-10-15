using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class HomeScreenEventCalendarTests
{
    private static readonly string[] Added =
    [
        "purge-order", "squig-smash", "against-the-tide", "trait-boost-rapid-assault", "trait-boost-flying",
        "trait-boost-psyker", "for-the-dark-gods", "for-the-emperor", "defeat-waves",
        "11th-edition-week-1", "11th-edition-week-2", "11th-edition-week-3", "global-operation-imperator",
    ];

    // Stale-calendar guard: fails once no HSE run ends after "today" (UTC). Fix by authoring the next
    // announced runs in Data/events/event-occurrences.json.
    [Fact]
    public void HseCalendarIsNotStale()
    {
        var snapshot = GameCatalogLoader.Load();
        var message = FindStaleCalendar(snapshot, DateTimeOffset.UtcNow);
        Assert.True(message is null, message);
    }

    [Fact]
    public void StaleCalendarHelperFailsForDateAfterLatestHseEnd()
    {
        var snapshot = GameCatalogLoader.Load();
        var message = FindStaleCalendar(snapshot, DateTimeOffset.Parse("2099-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(message);
        Assert.Contains("2026-11-14", message);
    }

    [Fact]
    public void HseDefinitionsAndUpdate142And143OccurrencesExist()
    {
        var snapshot = GameCatalogLoader.Load();
        var ids = snapshot.EventDefinitions.Where(d => d.Type == "HomeScreenEvent").Select(d => d.Id).ToHashSet();
        Assert.Equal(21, ids.Count(id => id.StartsWith("hse-", StringComparison.Ordinal)));
        foreach (var id in Added)
            Assert.Contains($"hse-{id}", ids);

        (string Def, string Start, string End)[] expected =
        [
            ("hse-against-the-tide", "2026-09-11T23:00:00Z", "2026-09-18T23:00:00Z"),
            ("hse-purge-order", "2026-09-21T23:00:00Z", "2026-09-25T23:00:00Z"),
            ("hse-squig-smash", "2026-09-27T23:00:00Z", "2026-09-30T23:00:00Z"),
            ("hse-machine-hunt", "2026-10-01T23:00:00Z", "2026-10-05T23:00:00Z"),
            ("hse-training-rush", "2026-10-05T23:00:00Z", "2026-10-09T23:00:00Z"),
            ("hse-against-the-tide", "2026-10-09T23:00:00Z", "2026-10-15T23:00:00Z"),
            ("hse-trait-boost-psyker", "2026-10-18T23:00:00Z", "2026-10-21T23:00:00Z"),
            ("hse-global-operation-imperator", "2026-10-26T00:00:00Z", "2026-11-01T00:00:00Z"),
            ("hse-faction-focus", "2026-10-26T00:00:00Z", "2026-11-01T00:00:00Z"),
            ("hse-warp-surge", "2026-11-05T00:00:00Z", "2026-11-08T00:00:00Z"),
            ("hse-training-rush", "2026-11-12T00:00:00Z", "2026-11-14T00:00:00Z"),
        ];
        foreach (var (def, start, end) in expected)
            Assert.Contains(snapshot.EventOccurrences, o =>
                o.DefinitionId == def && o.StartUtc == DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture) && o.EndUtc == DateTimeOffset.Parse(end, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string? FindStaleCalendar(GameCatalogSnapshot snapshot, DateTimeOffset now)
    {
        var hse = snapshot.EventDefinitions.Where(d => d.Type == "HomeScreenEvent").Select(d => d.Id).ToHashSet();
        var latest = snapshot.EventOccurrences.Where(o => hse.Contains(o.DefinitionId)).Max(o => o.EndUtc);
        return latest > now
            ? null
            : $"No Home Screen Event occurrence ends after {now:O}; latest HSE end is {latest:O}. Author the next announced runs in event-occurrences.json.";
    }
}
