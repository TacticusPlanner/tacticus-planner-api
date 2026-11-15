using TacticusPlanner.Api.Features.StagingTransfer;
using TacticusPlanner.Domain.RosterSnapshots;

namespace TacticusPlanner.Api.Tests;

public sealed class StagingTransferImporterTests
{
    private static StagingTransferPersonalRosterSnapshot Snapshot(RosterSnapshotSource source, string? importKey) =>
        new() { Name = "Snapshot", Source = source, ImportKey = importKey };

    [Fact]
    public void OnlyV1ImportedSnapshotsGetAnImportKey()
    {
        Assert.Null(StagingTransferImporter.ImportKeyFor(
            Snapshot(RosterSnapshotSource.Manual, "stray"), 0, new HashSet<string>()));
        Assert.Equal("v1:a", StagingTransferImporter.ImportKeyFor(
            Snapshot(RosterSnapshotSource.V1Import, "v1:a"), 0, new HashSet<string>()));
    }

    [Fact]
    public void AMissingOrRepeatedKeyIsReplacedWithAUniqueOne()
    {
        // A production that predates the key in the bundle sends none.
        var used = new HashSet<string>();
        var first = StagingTransferImporter.ImportKeyFor(Snapshot(RosterSnapshotSource.V1Import, null), 0, used);
        var repeated = StagingTransferImporter.ImportKeyFor(Snapshot(RosterSnapshotSource.V1Import, "v1:a"), 1, used);
        var again = StagingTransferImporter.ImportKeyFor(Snapshot(RosterSnapshotSource.V1Import, "v1:a"), 2, used);

        Assert.Equal("staging-transfer:0", first);
        Assert.Equal("v1:a", repeated);
        Assert.Equal("staging-transfer:2", again);
    }
}
