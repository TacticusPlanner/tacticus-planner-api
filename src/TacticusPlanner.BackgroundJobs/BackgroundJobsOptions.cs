namespace TacticusPlanner.BackgroundJobs;

public sealed class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>
    /// Runs a Hangfire server (the workers that execute queued jobs) in the host process. Off in the test hosts,
    /// which enqueue into real storage without executing; jobs are tested by calling them directly.
    /// </summary>
    public bool ServerEnabled { get; set; } = true;

    /// <summary>Workers per API replica. Each holds a storage connection only while it fetches or runs a job.</summary>
    public int WorkerCount { get; set; } = 2;

    /// <summary>
    /// Hangfire's own Npgsql pool per replica (to <c>hangfire-db</c>, separate from the API's <c>planner-db</c> pool). The infra budget
    /// (tacticus-planner-infra <c>modules/postgresql.bicep</c>) counts it: (11 API + 2 Hangfire) x 2 replicas +
    /// 2 jobs = 28 of the 35 connections Standard_B1ms leaves usable.
    /// </summary>
    public int StoragePoolSize { get; set; } = 2;
}
