using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>
/// The datamine gate: <c>Data/datamine/REF</c> pins a released commit (never <c>local</c>), and the copies under
/// <c>Data/datamine/raw/</c> are exactly the allowlisted files, unedited since <c>scripts/pull-datamine.cs</c>
/// recorded their checksums. Reads the source tree, so a hand edit fails here before it reaches a build.
/// </summary>
public sealed partial class DatamineSourceTests : IDisposable
{
    private readonly string _scratch = Path.Join(Path.GetTempPath(), $"datamine-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    [Fact]
    public void ShippedDatamineCopiesMatchTheirPinAndChecksums()
    {
        Assert.Empty(Check(ShippedDatamineDir()));
    }

    [Fact]
    public void ShippedAllowlistIsSortedWithoutDuplicates()
    {
        var paths = ReadAllowlist(Path.Join(ShippedDatamineDir(), "files.txt"));

        Assert.Equal(paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), paths);
    }

    [Fact]
    public void EmbeddedCopiesAreExactlyTheAllowlist()
    {
        var paths = ReadAllowlist(Path.Join(ShippedDatamineDir(), "files.txt"));

        Assert.Equal(paths, DatamineFiles.RawPaths);
    }

    [Fact]
    public void LocalPinIsRejected()
    {
        var dir = WriteTree("local ../tacticus-planner-data-mines\n");

        var error = Assert.Single(Check(dir));
        Assert.Contains("local", error, StringComparison.Ordinal);
        Assert.Contains("scripts/pull-datamine.cs", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v1.43.157.2989 e5c8c9b\n")]
    [InlineData("1.43.157.2989 e5c8c9bc749a7d1bbfb33a3d93056e8826e82678\n")]
    [InlineData("v1.43.157.2989 e5c8c9bc749a7d1bbfb33a3d93056e8826e82678\nv1.44 e5c8c9bc749a7d1bbfb33a3d93056e8826e82678\n")]
    [InlineData("v1.43.157.2989 E5C8C9BC749A7D1BBFB33A3D93056E8826E82678\n")]
    public void MalformedPinIsRejected(string pin)
    {
        var dir = WriteTree(pin);

        var error = Assert.Single(Check(dir));
        Assert.Contains("REF", error, StringComparison.Ordinal);
    }

    [Fact]
    public void UntaggedPinIsAccepted()
    {
        Assert.Empty(Check(WriteTree("untagged e5c8c9bc749a7d1bbfb33a3d93056e8826e82678\n")));
    }

    [Fact]
    public void HandEditedCopyIsRejected()
    {
        var dir = WriteTree();
        File.AppendAllText(Path.Join(dir, "raw", "gameconfig", "items.json"), " ");

        var error = Assert.Single(Check(dir));
        Assert.Contains("gameconfig/items.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCopyIsRejected()
    {
        var dir = WriteTree();
        File.Delete(Path.Join(dir, "raw", "gameconfig", "items.json"));

        Assert.Contains(Check(dir), error => error.Contains("gameconfig/items.json", StringComparison.Ordinal));
    }

    [Fact]
    public void UnlistedCopyIsRejected()
    {
        var dir = WriteTree();
        Directory.CreateDirectory(Path.Join(dir, "raw", "gameconfig", "units"));
        File.WriteAllText(Path.Join(dir, "raw", "gameconfig", "units", "lineup.json"), "{}");

        var error = Assert.Single(Check(dir));
        Assert.Contains("gameconfig/units/lineup.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksumListOutOfStepWithAllowlistIsRejected()
    {
        var dir = WriteTree();
        File.WriteAllText(Path.Join(dir, "files.txt"), "gameconfig/globalValues.json\n");
        File.Delete(Path.Join(dir, "raw", "gameconfig", "items.json"));

        Assert.Contains(Check(dir), error => error.Contains("files.sha256", StringComparison.Ordinal));
    }

    /// <summary>Every rule broken by the datamine folder <paramref name="dir"/>, one message each.</summary>
    internal static List<string> Check(string dir)
    {
        var errors = new List<string>();

        var refLines = File.ReadAllText(Path.Join(dir, "REF")).Split('\n');
        var pin = refLines[0];
        if (pin.StartsWith("local", StringComparison.Ordinal))
        {
            errors.Add($"REF is '{pin}': a local datamine pin cannot be merged. Pin a release with scripts/pull-datamine.cs.");
        }
        else if (refLines.Length != 2 || refLines[1].Length != 0 || !PinPattern().IsMatch(pin))
        {
            errors.Add("REF must be one LF-terminated line '<v-tag or untagged> <40 lowercase hex sha>'.");
        }

        var listed = ReadAllowlist(Path.Join(dir, "files.txt"));
        var rawDir = Path.Join(dir, "raw");
        var copies = Directory.Exists(rawDir)
            ? Directory.EnumerateFiles(rawDir, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(rawDir, file).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal)
            : [];

        errors.AddRange(listed.Where(path => !copies.Contains(path))
            .Select(path => $"raw/{path} is listed in files.txt but has no copy."));
        errors.AddRange(copies.Where(path => !listed.Contains(path)).Order(StringComparer.Ordinal)
            .Select(path => $"raw/{path} is not listed in files.txt."));

        var recorded = File.ReadAllLines(Path.Join(dir, "files.sha256"))
            .Select(line => line.Split("  ", 2))
            .ToList();
        if (!recorded.Select(parts => parts[^1]).SequenceEqual(listed))
        {
            errors.Add("files.sha256 does not list the files.txt paths in order. Run scripts/pull-datamine.cs.");
        }

        foreach (var parts in recorded.Where(parts => parts.Length == 2 && copies.Contains(parts[1])))
        {
            var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Join(rawDir, parts[1]))));
            if (actual != parts[0])
            {
                errors.Add($"raw/{parts[1]} differs from the pinned copy (checksum mismatch). Datamine copies are never edited by hand.");
            }
        }

        return errors;
    }

    internal static string ShippedDatamineDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Join(dir.FullName, "TacticusPlanner.slnx")))
            {
                return Path.Join(dir.FullName, "src", "TacticusPlanner.GameCatalog", "Data", "datamine");
            }
        }

        throw new InvalidOperationException("Repository root (TacticusPlanner.slnx) not found above the test output.");
    }

    private static List<string> ReadAllowlist(string file) =>
        File.ReadAllLines(file)
            .Select(line => (line.IndexOf('#', StringComparison.Ordinal) is var hash and >= 0 ? line[..hash] : line).Trim())
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>A copy of the shipped datamine folder in scratch space, optionally with another <c>REF</c>.</summary>
    private string WriteTree(string? pin = null)
    {
        var source = ShippedDatamineDir();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Join(_scratch, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        if (pin is not null)
        {
            File.WriteAllText(Path.Join(_scratch, "REF"), pin);
        }

        return _scratch;
    }

    [GeneratedRegex("^(v[0-9][0-9A-Za-z.\\-]*|untagged) [0-9a-f]{40}$")]
    private static partial Regex PinPattern();
}
