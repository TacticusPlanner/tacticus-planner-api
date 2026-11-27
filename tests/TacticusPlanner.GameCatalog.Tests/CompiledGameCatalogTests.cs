using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>The catalog compiled into the runtime assembly at build time serves exactly what an in-process compilation
/// of the same raw data serves (specs/game-catalog-compilation).</summary>
public sealed class CompiledGameCatalogTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 27, 15, 30, 0, TimeSpan.Zero);
    private static readonly Lazy<GameCatalogSnapshot> Compiled = new(() => GameCatalogCompiler.Compile(AsOf).ToSnapshot());

    public static TheoryData<string> CompiledDatasets => [.. GameCatalogServedViews.PropertiesByDataset.Keys];

    [Fact]
    public void EveryServedDatasetButTheCalendarIsCompiled()
    {
        Assert.Equal(
            GameCatalogDatasets.Served.Where(key => key != GameCatalogDatasets.EventsCalendar).Order(StringComparer.Ordinal),
            GameCatalogServedViews.PropertiesByDataset.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CompiledDatasets))]
    public void TheRuntimeServesTheCompiledPayload(string key)
    {
        var runtime = GameCatalogLoader.Load(AsOf);

        Assert.Equal(Serialize(Payload(Compiled.Value, key)), Serialize(Payload(runtime, key)));
        Assert.Equal(Compiled.Value.DatasetHashes[key], runtime.DatasetHashes[key]);
    }

    [Fact]
    public void TheRuntimeManifestAndCalendarMatchTheCompilation()
    {
        var runtime = GameCatalogLoader.Load(AsOf);

        Assert.Equal(Serialize(Compiled.Value.Manifest), Serialize(runtime.Manifest));
        Assert.Equal(Serialize(Compiled.Value.EventsCalendar), Serialize(runtime.EventsCalendar));
        Assert.Equal(Serialize(Compiled.Value.Runtime), Serialize(runtime.Runtime));
        Assert.Equal(GameCatalogRelease.Version, runtime.Version);
    }

    [Fact]
    public void ADayChangeReprojectsOnlyTheCalendar()
    {
        var today = GameCatalogLoader.Load(AsOf);
        var tomorrow = GameCatalogLoader.Load(AsOf.AddDays(1));

        Assert.Same(today.Views, tomorrow.Views);
        Assert.All(
            today.DatasetHashes.Where(pair => pair.Key != GameCatalogDatasets.EventsCalendar),
            pair => Assert.Equal(pair.Value, tomorrow.DatasetHashes[pair.Key]));
        Assert.Equal(
            Serialize(GameCatalogCompiler.Compile(AsOf.AddDays(1)).EventsCalendar),
            Serialize(tomorrow.EventsCalendar));
    }

    [Fact]
    public void TheWriterWritesEveryCompiledFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"compiled-catalog-{Guid.NewGuid():N}");
        try
        {
            GameCatalogCompiledWriter.Write(GameCatalogCompiler.Compile(AsOf), directory);

            Assert.True(File.Exists(Path.Combine(directory, GameCatalogCompiledFiles.Manifest)));
            Assert.True(File.Exists(Path.Combine(directory, GameCatalogCompiledFiles.Runtime)));
            Assert.All(GameCatalogServedViews.PropertiesByDataset.Keys, key =>
                Assert.True(File.Exists(Path.Combine(directory, GameCatalogCompiledFiles.Dataset(key)))));
            Assert.False(File.Exists(Path.Combine(directory, GameCatalogCompiledFiles.Dataset(GameCatalogDatasets.EventsCalendar))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static object? Payload(GameCatalogSnapshot snapshot, string key) =>
        typeof(GameCatalogServedViews).GetProperty(GameCatalogServedViews.PropertiesByDataset[key])!.GetValue(snapshot.Views);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, GameCatalogCompiledFiles.JsonOptions);
}
