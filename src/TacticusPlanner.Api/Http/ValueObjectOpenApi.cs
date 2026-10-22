using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TacticusPlanner.Api.Http;

/// <summary>
/// Describes each Vogen value object (<c>[ValueObject&lt;T&gt;]</c>) as the primitive it is written as on the wire.
/// The pipeline sees only a struct with no public members and emits an empty schema, so a generated client
/// would type a <c>CampaignBattleId</c> list as <c>unknown[]</c> (spec: openapi-contract).
/// </summary>
public static class ValueObjectOpenApi
{
    public static Task Apply(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken _)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (PrimitiveOf(type) is { } primitive)
        {
            Describe(schema, primitive);
        }
        // A collection of value objects loses its element schema entirely (no `items`); give it one.
        else if (schema.Items is null && context.JsonTypeInfo.ElementType is { } element
            && PrimitiveOf(Nullable.GetUnderlyingType(element) ?? element) is { } elementPrimitive)
        {
            var items = new OpenApiSchema();
            Describe(items, elementPrimitive);
            schema.Items = items;
        }

        return Task.CompletedTask;
    }

    private static void Describe(OpenApiSchema schema, Type primitive)
    {
        schema.Type = primitive == typeof(string) || primitive == typeof(Guid) ? JsonSchemaType.String : JsonSchemaType.Integer;
        schema.Format = primitive == typeof(Guid) ? "uuid" : null;
        schema.Properties = null;
    }

    private static Type? PrimitiveOf(Type type) =>
        type.GetCustomAttributes(inherit: false)
            .Select(attribute => attribute.GetType())
            .FirstOrDefault(attribute => attribute is { IsGenericType: true, Namespace: "Vogen" }
                && attribute.Name.StartsWith("ValueObjectAttribute", StringComparison.Ordinal))
            ?.GetGenericArguments()[0];
}
