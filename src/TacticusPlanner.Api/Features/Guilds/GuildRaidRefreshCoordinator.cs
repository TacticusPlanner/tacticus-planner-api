using System.Collections.Concurrent;
using TacticusPlanner.Domain.Guilds;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// De-duplicates concurrent Guild Raid refreshes for the same guild into one shared upstream operation.
/// The shared operation runs in its own DI scope with its own cancellation, isolated from every caller's
/// request: cancelling one caller's request must not cancel the flight other callers are waiting on, must
/// not dispose the scoped <see cref="GuildRaidStatusService"/>/<c>PlannerDbContext</c> the flight is still
/// using, and must not remove the flight from <see cref="flights"/> while it is still running (which would
/// let a later caller start a redundant second upstream call).
/// </summary>
public sealed class GuildRaidRefreshCoordinator(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<GuildRaidRefreshResult>>> flights = new();

    public Task<GuildRaidRefreshResult> RunAsync(Guild guild, CancellationToken ct)
    {
        var guildId = guild.Id.Value;
        var flight = flights.GetOrAdd(
            guildId,
            _ => new Lazy<Task<GuildRaidRefreshResult>>(
                () => RunAndRemoveAsync(guildId, guild),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return flight.Value.WaitAsync(ct);
    }

    private async Task<GuildRaidRefreshResult> RunAndRemoveAsync(Guid guildId, Guild guild)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<GuildRaidStatusService>();
            return await service.RefreshCoreAsync(guild, CancellationToken.None);
        }
        finally
        {
            flights.TryRemove(guildId, out _);
        }
    }
}
