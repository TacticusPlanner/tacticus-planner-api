using System.Globalization;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>
/// CI guard for the calendar weeks that date the event shops. Startup skips a <c>ShopEvent</c> week whose
/// <c>eventKey</c> has no event-shop merchant (so a datamine that drops an old event keeps loading); this test fails
/// while such a week has not ended yet — a typo in <c>eventKey</c>, or a calendar entry for a shop the pinned datamine
/// does not have yet. Fix the key or bump the datamine (see the game-catalog-data skill).
/// </summary>
public sealed class ShopEventCalendarTests
{
    [Fact]
    public void EveryCurrentOrUpcomingShopEventWeekHasAnEventShop()
    {
        var message = FindUndatedShopEventWeeks(GameCatalogLoader.Load(), DateTimeOffset.UtcNow);
        Assert.True(message is null, message);
    }

    [Fact]
    public void EveryPerWeekMerchantOfACalendaredEventHasACalendarWeek()
    {
        var message = FindMerchantWeeksMissingFromTheCalendar(Merchants(), GameCatalogLoader.Load());
        Assert.True(message is null, message);
    }

    /// <summary>
    /// Event keys the pinned datamine ships a merchant for whose calendar weeks are not authored yet. Each entry is a
    /// known gap (the event is not served); remove it when its weeks land in event-occurrences.json.
    /// </summary>
    private static readonly string[] KnownUndatedEventKeys = ["November2026"];

    [Fact]
    public void EveryCurrentOrUpcomingEventShopMerchantHasCalendarWeeks()
    {
        var merchants = Merchants()
            .Where(pair => !KnownUndatedEventKeys.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var message = FindCurrentMerchantsWithoutCalendarWeeks(merchants, GameCatalogLoader.Load(), DateTimeOffset.UtcNow);

        Assert.True(message is null, message);
    }

    [Fact]
    public void HelperFailsForAPerWeekMerchantBeyondTheCalendarWeeks()
    {
        var snapshot = GameCatalogLoader.Load();
        var calendared = GameCatalogDenormalizerAccess.ShopEventWeeksByEventKey(snapshot).First();
        var extraWeek = calendared.Value.Max(week => week.Week) + 1;
        var merchants = new Dictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)>(StringComparer.Ordinal)
        {
            [calendared.Key] = (null, new SortedDictionary<int, string>
            {
                [extraWeek] = $"{calendared.Key}Week{extraWeek}EventShop",
            }),
        };

        var message = FindMerchantWeeksMissingFromTheCalendar(merchants, snapshot);

        Assert.NotNull(message);
        Assert.Contains($"{calendared.Key}Week{extraWeek}EventShop", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HelperFailsForACurrentMerchantWithoutCalendarWeeksAndIgnoresPastOnes()
    {
        var snapshot = GameCatalogLoader.Load();
        var merchants = new Dictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)>(StringComparer.Ordinal)
        {
            ["December2099"] = ("December2099EventShop", new SortedDictionary<int, string>()),
            ["May2001"] = ("May2001EventShop", new SortedDictionary<int, string>()),
        };

        var message = FindCurrentMerchantsWithoutCalendarWeeks(
            merchants, snapshot, new DateTimeOffset(2099, 12, 3, 0, 0, 0, TimeSpan.Zero));

        Assert.NotNull(message);
        Assert.Contains("December2099", message, StringComparison.Ordinal);
        Assert.DoesNotContain("May2001", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HelperFailsForAnUpcomingWeekCutOffByAMissingPerWeekMerchant()
    {
        var snapshot = GameCatalogLoader.Load();
        var served = snapshot.RawEventShops.First(shop => shop.Weeks.Count > 1);

        // Serve only week 1, as the denormalizer does when week 2's merchant is missing.
        var message = FindUndatedShopEventWeeks(
            snapshot with { RawEventShops = [served with { Weeks = [served.Weeks[0]] }] },
            served.Weeks[0].Occurrence.StartUtc);

        Assert.NotNull(message);
        Assert.Contains($"{served.EventKey} week 2", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HelperFailsForAnUpcomingWeekWithoutAnEventShop()
    {
        var snapshot = GameCatalogLoader.Load();
        var start = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        var november = new GameCatalogEventOccurrence(
            "occ-2026-11-16-armageddon-shop-week1", "armageddon-shop", start, start.AddDays(7),
            new Dictionary<string, JsonElement>
            {
                ["weekNumber"] = JsonSerializer.SerializeToElement(1),
                ["eventKey"] = JsonSerializer.SerializeToElement("November2026"),
            });

        var message = FindUndatedShopEventWeeks(
            snapshot with { EventOccurrences = [.. snapshot.EventOccurrences, november] },
            new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.NotNull(message);
        Assert.Contains("November2026", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HelperIgnoresEndedWeeksWithoutAnEventShop()
    {
        var snapshot = GameCatalogLoader.Load();

        // The seeded weeks all have shops; drop them to simulate a datamine that no longer ships the merchants.
        var message = FindUndatedShopEventWeeks(
            snapshot with { RawEventShops = [] },
            new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Null(message);
    }

    private static IReadOnlyDictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)> Merchants() =>
        GameCatalogDenormalizerAccess.DatamineEventMerchants();

    private static string? FindUndatedShopEventWeeks(GameCatalogSnapshot snapshot, DateTimeOffset now)
    {
        // A calendar week is served only when its event has a merchant for it (per-week events stop at the first
        // week with none), so any unserved week that has not ended is a gap.
        var servedWeeks = snapshot.RawEventShops
            .SelectMany(shop => shop.Weeks.Select(week => (shop.EventKey, week.Week)))
            .ToHashSet();
        var unmatched = GameCatalogDenormalizerAccess.ShopEventWeeksByEventKey(snapshot)
            .SelectMany(pair => pair.Value.Select(week => (EventKey: pair.Key, week.Week, week.Occurrence)))
            .Where(entry => !servedWeeks.Contains((entry.EventKey, entry.Week)))
            .Where(entry => entry.Occurrence.EndUtc > now)
            .Select(entry => $"{entry.EventKey} week {entry.Week} ('{entry.Occurrence.Id}')")
            .ToArray();

        return unmatched.Length == 0
            ? null
            : $"ShopEvent weeks that have not ended have no event-shop merchant in the pinned datamine: {string.Join(", ", unmatched)}.";
    }

    /// <summary>Per-week merchants of a calendared event beyond its calendar weeks: the extra weeks are not served
    /// and their plans are rejected, so the calendar is missing weeks.</summary>
    private static string? FindMerchantWeeksMissingFromTheCalendar(
        IReadOnlyDictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)> merchants,
        GameCatalogSnapshot snapshot)
    {
        var calendar = GameCatalogDenormalizerAccess.ShopEventWeeksByEventKey(snapshot);
        var missing = merchants
            .Where(pair => calendar.ContainsKey(pair.Key))
            .SelectMany(pair => pair.Value.PerWeek
                .Where(week => calendar[pair.Key].All(calendarWeek => calendarWeek.Week != week.Key))
                .Select(week => week.Value))
            .ToArray();

        return missing.Length == 0
            ? null
            : $"Event-shop merchants have no ShopEvent calendar week: {string.Join(", ", missing)}. Add the weeks to event-occurrences.json.";
    }

    /// <summary>Live-event merchants for the current month or later whose event has no calendar weeks at all: the
    /// event is not served, so its shop is missing while it runs.</summary>
    private static string? FindCurrentMerchantsWithoutCalendarWeeks(
        IReadOnlyDictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)> merchants,
        GameCatalogSnapshot snapshot,
        DateTimeOffset now)
    {
        var calendar = GameCatalogDenormalizerAccess.ShopEventWeeksByEventKey(snapshot);
        var currentMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var undated = merchants.Keys
            .Where(eventKey => !calendar.ContainsKey(eventKey))
            .Where(eventKey => EventKeyMonth(eventKey) is { } month && month >= currentMonth)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return undated.Length == 0
            ? null
            : $"Event-shop merchants for the current month or later have no ShopEvent calendar weeks: {string.Join(", ", undated)}. Add them to event-occurrences.json (see the tp-calendar-events skill).";
    }

    private static DateTimeOffset? EventKeyMonth(string eventKey) =>
        DateTimeOffset.TryParseExact(
            eventKey, "MMMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var month)
            ? new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero)
            : null;
}

/// <summary>Reaches the internal calendar grouping the <c>event-shops</c> denormalizer uses.</summary>
internal static class GameCatalogDenormalizerAccess
{
    public static IReadOnlyDictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)> DatamineEventMerchants() =>
        Denormalization.GameCatalogDenormalizer.DatamineEventMerchants(GameCatalogLoader.LoadDatamineMerchants());

    public static IReadOnlyDictionary<string, IReadOnlyList<(int Week, GameCatalogEventOccurrence Occurrence)>>
        ShopEventWeeksByEventKey(GameCatalogSnapshot snapshot) =>
        Denormalization.GameCatalogDenormalizer.ShopEventWeeksByEventKey(snapshot.EventDefinitions, snapshot.EventOccurrences);
}
