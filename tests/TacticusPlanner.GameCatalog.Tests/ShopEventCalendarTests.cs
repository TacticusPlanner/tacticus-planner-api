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

    private static string? FindUndatedShopEventWeeks(GameCatalogSnapshot snapshot, DateTimeOffset now)
    {
        var servedKeys = snapshot.RawEventShops.Select(shop => shop.EventKey).ToHashSet(StringComparer.Ordinal);
        var unmatched = GameCatalogDenormalizerAccess.ShopEventWeeksByEventKey(snapshot)
            .Where(pair => !servedKeys.Contains(pair.Key))
            .SelectMany(pair => pair.Value.Select(week => (EventKey: pair.Key, week.Occurrence)))
            .Where(entry => entry.Occurrence.EndUtc > now)
            .Select(entry => $"{entry.EventKey} ('{entry.Occurrence.Id}')")
            .ToArray();

        return unmatched.Length == 0
            ? null
            : $"ShopEvent weeks that have not ended name an eventKey with no event-shop merchant in the pinned datamine: {string.Join(", ", unmatched)}.";
    }
}

/// <summary>Reaches the internal calendar grouping the <c>event-shops</c> denormalizer uses.</summary>
internal static class GameCatalogDenormalizerAccess
{
    public static IReadOnlyDictionary<string, IReadOnlyList<(int Week, GameCatalogEventOccurrence Occurrence)>>
        ShopEventWeeksByEventKey(GameCatalogSnapshot snapshot) =>
        Denormalization.GameCatalogDenormalizer.ShopEventWeeksByEventKey(snapshot.EventDefinitions, snapshot.EventOccurrences);
}
