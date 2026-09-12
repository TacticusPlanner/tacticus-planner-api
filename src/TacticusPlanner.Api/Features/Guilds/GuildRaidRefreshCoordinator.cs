using System.Collections.Concurrent;
using TacticusPlanner.Domain.Guilds;

namespace TacticusPlanner.Api.Features.Guilds;

public sealed class GuildRaidRefreshCoordinator
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<GuildRaidRefreshResult>>> flights = new();

    public async Task<GuildRaidRefreshResult> RunAsync(
        GuildId guildId,
        Func<Task<GuildRaidRefreshResult>> operation)
    {
        var flight = flights.GetOrAdd(
            guildId.Value,
            _ => new Lazy<Task<GuildRaidRefreshResult>>(operation, LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await flight.Value;
        }
        finally
        {
            flights.TryRemove(new KeyValuePair<Guid, Lazy<Task<GuildRaidRefreshResult>>>(guildId.Value, flight));
        }
    }
}
