using System.Threading.Channels;
using TacticusPlanner.Domain.Guilds;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Guilds whose past seasons should be checked for capture. In memory on purpose: a guild lost on restart, or
/// dropped when the queue is full, is enqueued again by its next refresh.
/// </summary>
public sealed class GuildRaidSeasonCaptureRequests
{
    private readonly Channel<GuildId> channel = Channel.CreateBounded<GuildId>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<GuildId> Reader => channel.Reader;

    public void Enqueue(GuildId guildId) => channel.Writer.TryWrite(guildId);
}

/// <summary>
/// Captures past seasons one guild at a time, outside any request.
/// </summary>
public sealed partial class GuildRaidSeasonCaptureWorker(
    GuildRaidSeasonCaptureRequests queue,
    IServiceScopeFactory scopeFactory,
    ILogger<GuildRaidSeasonCaptureWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var guildId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<GuildRaidSeasonCaptureService>()
                    .RunAsync(guildId, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogRunCrashed(logger, exception, guildId);
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Guild Raid season capture of guild {GuildId} crashed."
    )]
    private static partial void LogRunCrashed(ILogger logger, Exception exception, GuildId guildId);
}
