using System.Collections.Concurrent;
using TacticusPlanner.Api.Features.Analytics;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Factory-owned recording analytics; never sends events to an external destination.</summary>
internal sealed class RecordingProductAnalytics : IProductAnalytics
{
    private readonly ConcurrentBag<(string AnalyticsId, string EventName)> CapturedEvents = [];

    public void AccountRegistered(string analyticsId) => CapturedEvents.Add((analyticsId, "account_registered"));

    public void GuildRegistered(string analyticsId) => CapturedEvents.Add((analyticsId, "guild_registered"));

    public IReadOnlyList<string> GetEvents(string analyticsId) => CapturedEvents
        .Where(captured => captured.AnalyticsId == analyticsId)
        .Select(captured => captured.EventName)
        .ToList();
}
