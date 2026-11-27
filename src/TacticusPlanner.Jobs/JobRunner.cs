using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TacticusPlanner.Jobs;

/// <summary>Runs the batch job named by the first argument and turns the outcome into the process exit code.</summary>
public static partial class JobRunner
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const int UnknownJob = 2;
    public const int MigrationsPending = 3;

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var name = args.Count > 0 ? args[0] : "";
        await using var scope = services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetKeyedService<IBatchJob>(name);
        if (job is null)
        {
            LogUnknownJob(logger, name);
            return UnknownJob;
        }

        try
        {
            // The API applies migrations as it starts; a job running against an older or newer schema could
            // read or write it wrongly, so it waits for the next execution instead.
            var pending = await scope.ServiceProvider.GetRequiredService<IDatabaseReadiness>()
                .PendingMigrationsAsync(cancellationToken);
            if (pending.Count > 0)
            {
                LogMigrationsPending(logger, name, pending.Count);
                return MigrationsPending;
            }

            LogStarting(logger, name);
            await job.RunAsync(cancellationToken);
            LogSucceeded(logger, name);
            return Succeeded;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, name);
            return Failed;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unknown batch job '{Name}'.")]
    private static partial void LogUnknownJob(ILogger logger, string name);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Batch job {Name} did not run: {Count} database migrations are pending."
    )]
    private static partial void LogMigrationsPending(ILogger logger, string name, int count);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Batch job {Name} starting.")]
    private static partial void LogStarting(ILogger logger, string name);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Batch job {Name} succeeded.")]
    private static partial void LogSucceeded(ILogger logger, string name);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Batch job {Name} failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string name);
}
