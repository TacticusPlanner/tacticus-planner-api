using System.Text.Json;
using TacticusPlanner.GameCatalog.Utils;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GameCatalogHashingTests
{
    [Fact]
    public void CanonicalJsonHashIgnoresFormattingAndKeyOrderButChangesWithContent()
    {
        using var original = JsonDocument.Parse("""{"b":2,"a":["x","y"]}""");
        using var reformatted = JsonDocument.Parse("""
            {
              "a": [
                "x",
                "y"
              ],
              "b": 2
            }
            """);
        using var changed = JsonDocument.Parse("""{"b":3,"a":["x","y"]}""");

        var originalHash = GameCatalogHashing.ComputeCanonicalJsonHash(original.RootElement);

        // Whitespace and object key order do not affect the hash; a changed value does.
        Assert.Equal(originalHash, GameCatalogHashing.ComputeCanonicalJsonHash(reformatted.RootElement));
        Assert.NotEqual(originalHash, GameCatalogHashing.ComputeCanonicalJsonHash(changed.RootElement));
    }

    [Fact]
    public void CanonicalJsonHashIsArrayOrderSensitive()
    {
        using var first = JsonDocument.Parse("""["x","y"]""");
        using var reordered = JsonDocument.Parse("""["y","x"]""");

        // Arrays are ordered data, so reordering elements must change the hash.
        Assert.NotEqual(
            GameCatalogHashing.ComputeCanonicalJsonHash(first.RootElement),
            GameCatalogHashing.ComputeCanonicalJsonHash(reordered.RootElement));
    }

    [Fact]
    public void SnapshotHashChangesWhenAnyDatasetHashChanges()
    {
        var baseHashes = new Dictionary<string, string> { ["characters"] = "h1", ["mows"] = "h2" };
        var changedHashes = new Dictionary<string, string> { ["characters"] = "h1", ["mows"] = "h2-changed" };

        var baseHash = GameCatalogHashing.ComputeSnapshotHash("dev-1", 11, "1.40", baseHashes);

        Assert.Equal(baseHash, GameCatalogHashing.ComputeSnapshotHash("dev-1", 11, "1.40", baseHashes));
        Assert.NotEqual(baseHash, GameCatalogHashing.ComputeSnapshotHash("dev-1", 11, "1.40", changedHashes));
        Assert.NotEqual(baseHash, GameCatalogHashing.ComputeSnapshotHash("dev-2", 11, "1.40", baseHashes));
        Assert.NotEqual(baseHash, GameCatalogHashing.ComputeSnapshotHash("dev-1", 12, "1.40", baseHashes));
    }
}
