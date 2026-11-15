using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static readonly TimeSpan EventShopWeekLength = TimeSpan.FromDays(7);

    private static void ValidateEventShops(
        GameCatalogSnapshot snapshot, HashSet<string> unitOrMowIds, List<GameCatalogValidationError> errors) =>
        ValidateEventShops(snapshot.RawEventShops, snapshot.EventShopViews, unitOrMowIds, errors);

    /// <summary>
    /// Every served event shop must have weeks numbered 1..W that (when dated) are contiguous 7-day windows starting at 00:00 UTC, variants that meet every daily-shop rule, and a single cost
    /// currency <c>seasonalEventCurrency&lt;EventKey&gt;</c>. Every error names the event key. See
    /// specs/game-event-shops-dataset.
    /// </summary>
    internal static void ValidateEventShops(
        IReadOnlyList<GameCatalogRawEventShop> rawEventShops,
        IReadOnlyList<GameCatalogEventShopView> eventShopViews,
        HashSet<string> unitOrMowIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.EventShops;

        foreach (var eventShop in rawEventShops)
        {
            var eventKey = eventShop.EventKey;
            var weekNumbers = eventShop.Weeks.Select(week => week.Week).ToArray();

            if (!weekNumbers.SequenceEqual(Enumerable.Range(1, weekNumbers.Length)))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "InvalidEventShopWeeks",
                    $"Event '{eventKey}' has calendar weeks [{string.Join(", ", weekNumbers)}]; expected 1..{weekNumbers.Length}, each once."));
            }

            for (var index = 0; index < eventShop.Weeks.Count; index++)
            {
                var week = eventShop.Weeks[index];
                var occurrence = week.Occurrence;

                // An undated event (start date to be announced) has no calendar weeks to check.
                if (occurrence is not null && (occurrence.EndUtc - occurrence.StartUtc != EventShopWeekLength
                    || occurrence.StartUtc.UtcDateTime.TimeOfDay != TimeSpan.Zero))
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "InvalidEventShopWeek",
                        $"Event '{eventKey}' week {week.Week} ('{occurrence.Id}') must start at 00:00 UTC and last exactly 7 days."));
                }

                if (index > 0 && occurrence is not null
                    && occurrence.StartUtc != eventShop.Weeks[index - 1].Occurrence?.EndUtc)
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "InvalidEventShopWeeks",
                        $"Event '{eventKey}' week {week.Week} does not start when week {eventShop.Weeks[index - 1].Week} ends."));
                }

                // The denormalizer serves an event only up to its first week with a merchant, so this is only a
                // merchant that deserialized to null; a missing one is a CI calendar check, not a load failure.
                if (week.Merchant is null)
                {
                    continue;
                }

                ValidateRawShopVariants(dataset, $"{eventKey} week {week.Week}", week.Merchant, errors);

                var currency = GameCatalogDenormalizer.EventShopCurrency(eventKey);
                var otherCurrencies = week.Merchant.Products
                    .SelectMany(slot => slot)
                    .Select(variant => variant.Cost?.Type)
                    .Where(type => type is not null && !string.Equals(type, currency, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (otherCurrencies.Length > 0)
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "EventShopCurrencyMismatch",
                        $"Event '{eventKey}' week {week.Week} has variants costing [{string.Join(", ", otherCurrencies)}]; expected only '{currency}'."));
                }
            }
        }

        foreach (var eventShop in eventShopViews)
        {
            foreach (var week in eventShop.Weeks)
            {
                ValidateShopSlotViews(dataset, $"{eventShop.EventKey} week {week.Week}", week.Slots, unitOrMowIds, errors);
            }
        }
    }
}
