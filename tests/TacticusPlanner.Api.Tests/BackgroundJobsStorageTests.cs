using Microsoft.Extensions.Configuration;
using Npgsql;
using TacticusPlanner.BackgroundJobs;

namespace TacticusPlanner.Api.Tests;

public sealed class BackgroundJobsStorageTests
{
    private const string Planner = "Host=db;Database=tacticus_planner;Username=u;Password=p;Maximum Pool Size=11";
    private const string Hangfire = "Host=db;Database=tacticus_planner_hangfire;Username=u;Password=p;Maximum Pool Size=11";

    [Fact]
    public void UsesHangfiresOwnDatabaseWhenConfigured()
    {
        var builder = Storage(new() { ["ConnectionStrings:planner-db"] = Planner, ["ConnectionStrings:hangfire-db"] = Hangfire });

        Assert.Equal("tacticus_planner_hangfire", builder.Database);
    }

    [Fact]
    public void FallsBackToThePlannerDatabaseWithoutOne()
    {
        var builder = Storage(new() { ["ConnectionStrings:planner-db"] = Planner });

        Assert.Equal("tacticus_planner", builder.Database);
    }

    [Fact]
    public void KeepsItsOwnPoolSizeWhateverTheConnectionStringSays()
    {
        var builder = Storage(new() { ["ConnectionStrings:hangfire-db"] = Hangfire }, storagePoolSize: 2);

        Assert.Equal(2, builder.MaxPoolSize);
        Assert.Equal("tacticus-planner-hangfire", builder.ApplicationName);
    }

    [Fact]
    public void RequiresAConnectionString()
    {
        Assert.Throws<InvalidOperationException>(() => Storage([]));
    }

    private static NpgsqlConnectionStringBuilder Storage(Dictionary<string, string?> settings, int storagePoolSize = 2)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new BackgroundJobsOptions { StoragePoolSize = storagePoolSize };
        return new NpgsqlConnectionStringBuilder(DependencyInjection.StorageConnectionString(configuration, options));
    }
}
