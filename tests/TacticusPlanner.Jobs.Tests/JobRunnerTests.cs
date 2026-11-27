using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TacticusPlanner.Jobs.Tests;

public sealed class JobRunnerTests
{
    [Fact]
    public async Task RunsTheNamedJob()
    {
        var job = new RecordingJob();

        var exitCode = await RunAsync(job, pendingMigrations: [], "recording");

        Assert.Equal(JobRunner.Succeeded, exitCode);
        Assert.Equal(1, job.Runs);
    }

    [Theory]
    [InlineData]
    [InlineData("missing")]
    public async Task RefusesAMissingOrUnknownJobName(params string[] args)
    {
        var job = new RecordingJob();

        var exitCode = await RunAsync(job, pendingMigrations: [], args);

        Assert.Equal(JobRunner.UnknownJob, exitCode);
        Assert.Equal(0, job.Runs);
    }

    [Fact]
    public async Task DoesNotRunWhileMigrationsArePending()
    {
        var job = new RecordingJob();

        var exitCode = await RunAsync(job, pendingMigrations: ["20261127000000_Next"], "recording");

        Assert.Equal(JobRunner.MigrationsPending, exitCode);
        Assert.Equal(0, job.Runs);
    }

    [Fact]
    public async Task ReportsAFailedJob()
    {
        var job = new RecordingJob(fail: true);

        var exitCode = await RunAsync(job, pendingMigrations: [], "recording");

        Assert.Equal(JobRunner.Failed, exitCode);
        Assert.Equal(1, job.Runs);
    }

    private static async Task<int> RunAsync(RecordingJob job, IReadOnlyList<string> pendingMigrations, params string[] args)
    {
        var services = new ServiceCollection()
            .AddKeyedSingleton<IBatchJob>("recording", job)
            .AddSingleton<IDatabaseReadiness>(new FixedReadiness(pendingMigrations))
            .BuildServiceProvider();
        await using (services)
        {
            return await JobRunner.RunAsync(services, args, NullLogger.Instance, TestContext.Current.CancellationToken);
        }
    }

    private sealed class RecordingJob(bool fail = false) : IBatchJob
    {
        public int Runs { get; private set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Runs++;
            return fail ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        }
    }

    private sealed class FixedReadiness(IReadOnlyList<string> pending) : IDatabaseReadiness
    {
        public Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(pending);
    }
}
