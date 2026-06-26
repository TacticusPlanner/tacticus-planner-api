using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TacticusPlanner.GameCatalog;

public static class GameCatalogHashing
{
    public static string ComputeCanonicalJsonHash<T>(T value, JsonSerializerOptions options)
    {
        var element = JsonSerializer.SerializeToElement(value, options);

        return ComputeCanonicalJsonHash(element);
    }

    public static string ComputeCanonicalJsonHash(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalValue(writer, element);
        }

        return ComputeSha256Hex(stream.ToArray());
    }

    public static string ComputeSnapshotHash(
        string version,
        int schemaVersion,
        string gameVersion,
        IReadOnlyDictionary<string, string> datasetHashes
    )
    {
        var builder = new StringBuilder()
            .Append("version:")
            .Append(version)
            .Append('\n')
            .Append("schemaVersion:")
            .Append(schemaVersion.ToString(CultureInfo.InvariantCulture))
            .Append('\n')
            .Append("gameVersion:")
            .Append(gameVersion)
            .Append('\n');

        foreach (var (key, hash) in datasetHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder
                .Append(key)
                .Append(':')
                .Append(hash)
                .Append('\n');
        }

        return ComputeSha256Hex(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    public static string ComputeQueryHash(string datasetHash, IEnumerable<KeyValuePair<string, string>> normalizedQuery)
    {
        var builder = new StringBuilder()
            .Append("dataset:")
            .Append(datasetHash)
            .Append('\n');

        foreach (var (key, value) in normalizedQuery.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder
                .Append(key)
                .Append('=')
                .Append(value)
                .Append('\n');
        }

        return ComputeSha256Hex(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalValue(writer, item);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
