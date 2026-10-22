using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FastEndpoints;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// The OpenAPI document is the client's code-generation contract (spec: openapi-contract): schema names are
/// short and unique. The checks walk every endpoint's request/response types, the same types the document
/// describes, so they fail with the CLR types named rather than with a silently renamed schema.
/// </summary>
public sealed class OpenApiDocumentTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    [Fact]
    public async Task SchemaNamesCarryNoNamespacePrefix()
    {
        var schemas = await SchemasAsync();

        Assert.Contains("GoalDetailResponse", schemas.Select(schema => schema.Key));
        Assert.DoesNotContain(schemas, schema =>
            schema.Key.StartsWith("TacticusPlanner", StringComparison.Ordinal)
            || schema.Key.StartsWith("System", StringComparison.Ordinal));
    }

    [Fact]
    public void NoTwoContractTypesShareASchemaName()
    {
        var collisions = ContractTypes()
            .Where(type => !IsInlined(type))
            .GroupBy(SchemaName)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(type => type.FullName))}")
            .ToList();

        Assert.True(collisions.Count == 0, "Schema name collisions:\n" + string.Join("\n", collisions));
    }

    [Fact]
    public async Task EveryContractObjectTypeHasItsShortNamedSchema()
    {
        // Keeps SchemaName (and so the collision check) in step with the names the document really uses.
        var names = (await SchemasAsync()).Select(schema => schema.Key).ToHashSet(StringComparer.Ordinal);

        // Response types only: a request bound wholly from route/query (GET, DELETE) has no body schema.
        var missing = ContractTypes(responsesOnly: true)
            .Where(type => !IsInlined(type) && !type.IsEnum && !type.IsGenericType)
            .Select(SchemaName)
            .Where(name => !names.Contains(name))
            .ToList();

        Assert.True(missing.Count == 0, "Contract types without a schema: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task EnumBackedPropertiesAreStringEnums()
    {
        var schemas = await SchemasAsync();

        var plainStrings = (
            from type in ContractTypes()
            where !IsInlined(type) && !type.IsEnum && !type.IsGenericType && schemas.ContainsKey(SchemaName(type))
            from property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            where EnumOf(property.PropertyType) is not null
            let name = JsonNamingPolicy.CamelCase.ConvertName(property.Name)
            let schema = schemas[SchemaName(type)]!["properties"]?[name]
            where schema is not null && !IsStringEnum(schema, schemas)
            select $"{SchemaName(type)}.{name}").ToList();

        Assert.True(plainStrings.Count == 0, "Enum-backed properties without an enum schema: " + string.Join(", ", plainStrings));
    }

    [Theory]
    [InlineData("/api/v1/goals", "archived")]
    [InlineData("/api/v1/projects", "archived")]
    [InlineData("/api/v1/legendary-event-plans/{eventId}/run-records", "limit")]
    [InlineData("/api/v1/legendary-event-plans/{eventId}/run-records", "after")]
    public async Task QueryParametersAreDocumented(string path, string parameter)
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        var parameters = JsonNode.Parse(json)!["paths"]![path]!["get"]!["parameters"]?.AsArray() ?? [];

        Assert.Contains(parameters, entry => entry?["in"]?.GetValue<string>() == "query"
            && entry["name"]?.GetValue<string>() == parameter);
    }

    [Fact]
    public async Task PlannerResponsesListEveryPropertyAsRequired()
    {
        var document = JsonNode.Parse(
            await factory.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        var schemas = document["components"]!["schemas"]!.AsObject();

        // Every property of a planner (non-catalog) response is always written (null included), so a generated client must not
        // see it as optional. Exempt: ApiProblem, whose members depend on the status.
        var pending = new Stack<string>(document["paths"]!.AsObject()
            .Where(path => !path.Key.StartsWith("/api/v1/game-catalog", StringComparison.Ordinal))
            .SelectMany(path => path.Value!.AsObject().Select(operation => operation.Value!["responses"]))
            .SelectMany(responses => References(responses!)));
        var seen = new HashSet<string>();
        var optional = new List<string>();
        while (pending.TryPop(out var name))
        {
            if (!seen.Add(name) || name == ProblemDetailsOpenApiName)
                continue;
            var schema = schemas[name]!;
            var required = schema["required"]?.AsArray().Select(entry => entry!.GetValue<string>()).ToHashSet() ?? [];
            optional.AddRange((schema["properties"]?.AsObject() ?? []).Select(property => property.Key)
                .Where(property => !required.Contains(property))
                .Select(property => $"{name}.{property}"));
            foreach (var reference in References(schema))
                pending.Push(reference);
        }

        Assert.True(optional.Count == 0, "Response properties not listed as required: " + string.Join(", ", optional));
    }

    private const string ProblemDetailsOpenApiName = "ApiProblem";

    private static IEnumerable<string> References(JsonNode node) =>
        Nodes(node, "")
            .Select(entry => (entry.Node as JsonObject)?["$ref"]?.GetValue<string>())
            .OfType<string>()
            .Select(reference => reference.Split('/')[^1]);

    private static readonly string[] ShapeKeywords =
        ["type", "properties", "enum", "oneOf", "anyOf", "allOf", "$ref", "additionalProperties"];

    [Fact]
    public async Task EverySchemaDescribesItsShape()
    {
        var schemas = await SchemasAsync();

        // An empty schema or an array without items generates as `unknown`. Raw JSON (JsonElement) is the one
        // legitimately shapeless value.
        var shapeless = schemas
            .Where(schema => !schema.Key.Contains("JsonElement", StringComparison.Ordinal))
            .Where(schema => schema.Value is JsonObject shape
                && (shape["type"]?.ToString() == "array" ? shape["items"] is null
                    : !ShapeKeywords.Any(shape.ContainsKey)))
            .Select(schema => schema.Key)
            .ToList();

        Assert.True(shapeless.Count == 0, "Schemas without a shape: " + string.Join(", ", shapeless));
    }

    [Fact]
    public async Task NullableReferencesKeepTheirNull()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        // A `type` that includes null next to a oneOf/anyOf/$ref is generated as (null | object) & Ref, losing the null.
        var lossy = Nodes(JsonNode.Parse(json)!, "")
            .Where(entry => entry.Node is JsonObject schema
                && ((schema["type"] is JsonArray types && types.Any(type => type?.GetValue<string>() == "null")
                        && (schema["oneOf"] is not null || schema["anyOf"] is not null || schema["$ref"] is not null))
                    // A one-entry oneOf is how the pipeline marks a nullable reference that lost its null.
                    || schema["oneOf"] is JsonArray { Count: 1 }))
            .Select(entry => entry.Path)
            .ToList();

        Assert.True(lossy.Count == 0, "Nullable references that lose null: " + string.Join(", ", lossy));
    }

    private static IEnumerable<(string Path, JsonNode Node)> Nodes(JsonNode node, string path)
    {
        yield return (path, node);
        var children = node switch
        {
            JsonObject obj => obj.Select(child => (Path: $"{path}/{child.Key}", child.Value)),
            JsonArray array => array.Select((child, index) => (Path: $"{path}/{index}", Value: child)),
            _ => [],
        };
        foreach (var (childPath, child) in children)
        {
            if (child is not null)
            {
                foreach (var entry in Nodes(child, childPath))
                    yield return entry;
            }
        }
    }

    /// <summary>The enum a property carries, directly, nullable, or as a collection's element.</summary>
    private static Type? EnumOf(Type type)
    {
        var element = type.IsArray ? type.GetElementType()!
            : type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) ? type.GetGenericArguments()[^1]
            : type;
        element = Nullable.GetUnderlyingType(element) ?? element;
        return element.IsEnum ? element : null;
    }

    /// <summary>An inline <c>enum</c>, a <c>$ref</c> to one, a nullable <c>oneOf</c>/<c>anyOf</c> wrapper or an array of them.</summary>
    private static bool IsStringEnum(JsonNode schema, JsonObject schemas)
    {
        if (schema["enum"] is not null)
            return true;
        if (schema["$ref"]?.GetValue<string>() is { } reference)
            return schemas[reference.Split('/')[^1]]?["enum"] is not null;
        if (schema["items"] is { } items)
            return IsStringEnum(items, schemas);
        var options = (schema["oneOf"] ?? schema["anyOf"])?.AsArray();
        return options is not null && options.Any(option => option is not null && IsStringEnum(option, schemas));
    }

    private async Task<JsonObject> SchemasAsync()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        return JsonNode.Parse(json)!["components"]!["schemas"]!.AsObject();
    }

    /// <summary>FastEndpoints' short name: the type name, generics as <c>NameOfArgAndArg</c>.</summary>
    internal static string SchemaName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;
        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}Of{string.Join("And", type.GetGenericArguments().Select(SchemaName))}";
    }

    /// <summary>Types the document writes inline instead of as a named schema.</summary>
    private static bool IsInlined(Type type) =>
        type.IsPrimitive
        || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeSpan)
        || type == typeof(object) || type == typeof(JsonNode) || type.Namespace == "System.Text.Json"
        || Nullable.GetUnderlyingType(type) is not null
        || (type.IsValueType && !type.IsEnum) // Vogen ids and other value objects serialize as their primitive
        || type.IsArray
        || (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) && !type.Name.StartsWith("IReadOnlyDictionary", StringComparison.Ordinal)
            && !type.Name.StartsWith("Dictionary", StringComparison.Ordinal));

    /// <summary>Every type reachable from an endpoint's request or response, walking properties and generic arguments.</summary>
    internal static IReadOnlyCollection<Type> ContractTypes(bool responsesOnly = false)
    {
        var roots = typeof(Program).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true } && typeof(IEndpoint).IsAssignableFrom(type))
            .SelectMany(endpoint => EndpointContractTypes(endpoint, responsesOnly));

        var seen = new HashSet<Type>();
        var pending = new Stack<Type>(roots);
        while (pending.TryPop(out var type))
        {
            if (type.IsGenericParameter || !seen.Add(type))
                continue;
            foreach (var argument in type.IsArray ? [type.GetElementType()!] : type.GetGenericArguments())
                pending.Push(argument);
            if (IsInlined(type) || type.IsEnum || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                continue;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                pending.Push(property.PropertyType);
        }

        return seen.Where(type => type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true || type.IsGenericType).ToList();
    }

    /// <summary>The request/response arguments of the endpoint's FastEndpoints base (<c>Endpoint&lt;TRequest, TResponse[, TMapper]&gt;</c>,
    /// <c>EndpointWithoutRequest&lt;TResponse&gt;</c>), skipping mappers and the empty placeholders.</summary>
    private static IEnumerable<Type> EndpointContractTypes(Type endpoint, bool responsesOnly)
    {
        for (var type = endpoint.BaseType; type is not null; type = type.BaseType)
        {
            if (!type.IsGenericType || type.Assembly != typeof(IEndpoint).Assembly)
                continue;
            var arguments = type.GetGenericArguments()
                .Where(argument => !typeof(IMapper).IsAssignableFrom(argument))
                .ToArray();
            var withoutRequest = type.Name.StartsWith("EndpointWithoutRequest", StringComparison.Ordinal);
            foreach (var (argument, index) in arguments.Select((argument, index) => (argument, index)))
            {
                var isResponse = withoutRequest || index == 1;
                if ((isResponse || !responsesOnly)
                    && argument != typeof(EmptyRequest) && argument != typeof(EmptyResponse) && argument != typeof(object))
                    yield return argument;
            }
        }
    }
}
