using System.Collections.Concurrent;
using TacticusPlanner.Api.Features.Analytics;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Stands in for the real PostHog-backed <see cref="IProductAnalytics"/> so endpoint tests can assert what
/// was captured without an outbound request. Registered as a singleton shared across every test in a
/// <see cref="PlannerApiFactory"/> fixture, so captured events are keyed by analytics id rather than
/// cleared between tests — analytics ids are derived per-account and every test uses its own unique
/// account, so entries never collide across tests (same pattern as <see cref="FakeTacticusApi"/>'s
/// per-token dictionaries).
/// </summary>
internal sealed class RecordingProductAnalytics : IProductAnalytics
{
    private static readonly ConcurrentBag<(string AnalyticsId, string EventName)> CapturedEvents = [];

    public void AccountRegistered(string analyticsId) => CapturedEvents.Add((analyticsId, "account_registered"));

    public void GuildRegistered(string analyticsId) => CapturedEvents.Add((analyticsId, "guild_registered"));

    public static IReadOnlyList<string> GetEvents(string analyticsId) => CapturedEvents
        .Where(captured => captured.AnalyticsId == analyticsId)
        .Select(captured => captured.EventName)
        .ToList();
}
