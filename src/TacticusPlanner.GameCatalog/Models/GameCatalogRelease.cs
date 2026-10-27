using System.Security.Cryptography;
using System.Text;

namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// Release metadata for the embedded game catalog, derived from the embedded data so nothing is bumped by
/// hand. The served schema hash is derived separately from the payload types (see
/// <c>GameCatalogContractHashing</c>).
/// </summary>
public static class GameCatalogRelease
{
    /// <summary>The game build the pinned datamine was extracted from (its <c>raw/VERSION</c>, e.g.
    /// <c>1.43.157.2989</c>).</summary>
    public static string GameVersion { get; } = ReadDatamineVersion();

    /// <summary>Release tag of the embedded snapshot: <see cref="GameVersion"/> plus a short digest of every
    /// embedded data file, e.g. <c>1.43.157.2989+3f2a9c01b7de</c>. Any datamine or curated change moves it;
    /// a code-only change does not.</summary>
    public static string Version { get; } = $"{GameVersion}+{ComputeDataDigest()[..12]}";

    private static string ReadDatamineVersion()
    {
        using var reader = new StreamReader(DatamineFiles.Open("VERSION"));
        var version = reader.ReadToEnd().Trim();

        return version.Length > 0
            ? version
            : throw new InvalidOperationException("Datamine file 'raw/VERSION' is empty.");
    }

    private static string ComputeDataDigest()
    {
        var assembly = typeof(GameCatalogRelease).Assembly;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            hash.AppendData(Encoding.UTF8.GetBytes($"{name}\n{buffer.Length}\n"));
            hash.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
