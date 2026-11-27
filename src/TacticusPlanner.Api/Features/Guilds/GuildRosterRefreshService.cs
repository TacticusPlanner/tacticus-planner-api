using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Refreshes a guild's roster from a raid refresh once it is a day old, so departures and demotions reach Planner
/// without a manual sync. Runs inside a refresh that already succeeded.
/// </summary>
public sealed partial class GuildRosterRefreshService(
    PlannerDbContext db,
    GuildSyncService syncService,
    TimeProvider timeProvider,
    ILogger<GuildRosterRefreshService> logger)
{
    /// <summary>How old the last successful roster sync must be.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Spacing between the attempts this starts.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    /// <summary>Refreshes the roster when due. Never throws, except on cancellation.</summary>
    public async Task RunIfDueAsync(GuildId guildId, CancellationToken ct)
    {
        try
        {
            if (!await ClaimAsync(guildId, ct))
            {
                return;
            }

            db.ChangeTracker.Clear();
            if (await syncService.RefreshRosterAsync(guildId, ct) is { } failure)
            {
                LogRefreshFailed(logger, guildId, failure.GetType().Name);
            }
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            LogRefreshFailed(logger, guildId, exception.GetType().Name);
        }
    }

    /// <summary>
    /// Claims the attempt on <see cref="Guild.LastSyncAttemptedAt"/>, which also opens the one-minute cooldown of
    /// <c>POST guild/sync</c>, so the two never fetch the roster at once.
    /// </summary>
    private async Task<bool> ClaimAsync(GuildId guildId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var staleBefore = now - MaxAge;
        var retryBefore = now - RetryDelay;
        return await db.Guilds
            .Where(guild => guild.Id == guildId
                && guild.GuildApiToken != null
                && guild.LastSyncSucceededAt != null
                && guild.LastSyncSucceededAt <= staleBefore
                && (guild.LastSyncAttemptedAt == null || guild.LastSyncAttemptedAt <= retryBefore))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(guild => guild.LastSyncAttemptedAt, now)
                .SetProperty(guild => guild.Revision, guild => guild.Revision + 1), ct) > 0;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The daily roster refresh of guild {GuildId} failed: {Failure}."
    )]
    private static partial void LogRefreshFailed(ILogger logger, GuildId guildId, string failure);
}
