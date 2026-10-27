using System.Text.Json;
using TacticusPlanner.GameCatalog.Utils;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GameCatalogContractHashingTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static string Hash<T>() =>
        GameCatalogContractHashing.ComputeContractHash(new Dictionary<string, Type> { ["items"] = typeof(T) }, Options);

    [Fact]
    public void ContractHashIsStableAndIgnoresTypeNames()
    {
        Assert.Equal(Hash<IReadOnlyList<Base>>(), Hash<IReadOnlyList<Base>>());
        Assert.Equal(Hash<IReadOnlyList<Base>>(), Hash<IReadOnlyList<RenamedBase>>());
    }

    [Fact]
    public void ContractHashChangesWithTheServedShape()
    {
        var baseHash = Hash<IReadOnlyList<Base>>();

        Assert.NotEqual(baseHash, Hash<IReadOnlyList<AddedField>>());
        Assert.NotEqual(baseHash, Hash<IReadOnlyList<RenamedField>>());
        Assert.NotEqual(baseHash, Hash<IReadOnlyList<ChangedType>>());
        Assert.NotEqual(baseHash, Hash<IReadOnlyList<NullableField>>());
        Assert.NotEqual(baseHash, Hash<IReadOnlyList<NestedEnumAdded>>());
        Assert.NotEqual(baseHash, Hash<Base>());
    }

    [Fact]
    public void ContractHashCoversDatasetKeys()
    {
        var hash = GameCatalogContractHashing.ComputeContractHash(
            new Dictionary<string, Type> { ["a"] = typeof(Base) }, Options);

        Assert.NotEqual(hash, GameCatalogContractHashing.ComputeContractHash(
            new Dictionary<string, Type> { ["b"] = typeof(Base) }, Options));
    }

    public enum Kind { One, Two }

    public enum KindWithThree { One, Two, Three }

    public sealed record Nested(Kind Kind);

    public sealed record NestedWithThree(KindWithThree Kind);

    public sealed record Base(string Id, int Count, Nested Nested);

    public sealed record RenamedBase(string Id, int Count, Nested Nested);

    public sealed record AddedField(string Id, int Count, Nested Nested, string Extra);

    public sealed record RenamedField(string Key, int Count, Nested Nested);

    public sealed record ChangedType(string Id, double Count, Nested Nested);

    public sealed record NullableField(string? Id, int Count, Nested Nested);

    public sealed record NestedEnumAdded(string Id, int Count, NestedWithThree Nested);
}
