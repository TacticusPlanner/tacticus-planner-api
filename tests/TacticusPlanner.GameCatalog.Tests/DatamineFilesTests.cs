using System.Text.Json;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class DatamineFilesTests
{
    [Fact]
    public void OpensAPinnedCopyByItsRawPath()
    {
        using var stream = DatamineFiles.Open("gameconfig/items.json");
        using var document = JsonDocument.Parse(stream);

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.NotEmpty(document.RootElement.EnumerateObject());
    }

    [Fact]
    public void UnlistedPathThrowsNamingTheAllowlist()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DatamineFiles.Open("gameconfig/units/xpLevels.json"));

        Assert.Contains("raw/gameconfig/units/xpLevels.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("files.txt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DatamineCopiesNeverMatchTheLeafNameConvention()
    {
        // The catalog loader matches a key's file by ".{leaf}". Datamine resources keep '/' separators, so a game
        // leaf name (items.json, units.json, factions.json) can never bind to, or collide with, a catalog key.
        var datamine = typeof(DatamineFiles).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(DatamineFiles.ResourcePrefix, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(datamine);
        Assert.All(datamine, name =>
        {
            Assert.DoesNotContain('\\', name);
            Assert.False(name.EndsWith($".{name.Split('/')[^1]}", StringComparison.Ordinal), name);
        });
    }

    [Fact]
    public void CatalogStillLoadsWithDatamineCopiesEmbedded()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.NotEmpty(snapshot.Manifest.Datasets);
    }
}
