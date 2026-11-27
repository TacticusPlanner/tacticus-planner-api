using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TacticusPlanner.GameCatalog.Utils;

/// <summary>
/// Derives the served schema hash from the JSON contract of each dataset's payload type, so a shape change
/// (an added, removed or renamed field, a type or nullability change, a new enum member) moves the hash with
/// no hand-maintained version. Object types are numbered in the order they are first reached rather than
/// named, so renaming a C# type alone does not change it. Opaque values (primitives, <see cref="JsonElement"/>
/// passthrough) contribute their type name only.
/// </summary>
public static class GameCatalogContractHashing
{
    public static string ComputeContractHash(IReadOnlyDictionary<string, Type> payloadTypes, JsonSerializerOptions options)
    {
        // Describe with nullable annotations honoured so nullability is part of the contract. The copy is only
        // read for metadata; serialization keeps using the caller's options.
        var describeOptions = new JsonSerializerOptions(options)
        {
            RespectNullableAnnotations = true,
            TypeInfoResolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver(),
        };

        var ids = new Dictionary<Type, int>();
        var pending = new Queue<Type>();
        var builder = new StringBuilder();

        string Describe(Type type)
        {
            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                return Describe(underlying) + "?";
            }

            if (type.IsEnum)
            {
                return "enum[" + string.Join(',', Enum.GetNames(type)) + "]";
            }

            var info = describeOptions.GetTypeInfo(type);
            switch (info.Kind)
            {
                case JsonTypeInfoKind.Enumerable:
                    return "[" + Describe(info.ElementType!) + "]";
                case JsonTypeInfoKind.Dictionary:
                    return "{" + Describe(info.KeyType!) + ":" + Describe(info.ElementType!) + "}";
                case JsonTypeInfoKind.Object:
                    if (!ids.TryGetValue(type, out var id))
                    {
                        id = ids.Count;
                        ids[type] = id;
                        pending.Enqueue(type);
                    }

                    return "#" + id;
                default:
                    return type.Name;
            }
        }

        foreach (var (key, type) in payloadTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(key).Append('=').Append(Describe(type)).Append('\n');
        }

        while (pending.TryDequeue(out var type))
        {
            var info = describeOptions.GetTypeInfo(type);
            builder.Append('#').Append(ids[type]).Append('{');
            foreach (var property in info.Properties.OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                builder
                    .Append(property.Name)
                    .Append(property.IsRequired ? "!" : "")
                    .Append(':')
                    .Append(Describe(property.PropertyType))
                    .Append(property.IsGetNullable && !property.PropertyType.IsValueType ? "?" : "")
                    .Append(';');
            }

            if (info.PolymorphismOptions is { } polymorphism)
            {
                builder.Append("derived:").Append(polymorphism.TypeDiscriminatorPropertyName).Append('(');
                foreach (var derived in polymorphism.DerivedTypes.OrderBy(derived => derived.TypeDiscriminator?.ToString(), StringComparer.Ordinal))
                {
                    builder.Append(derived.TypeDiscriminator).Append('=').Append(Describe(derived.DerivedType)).Append(',');
                }

                builder.Append(')');
            }

            builder.Append("}\n");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
