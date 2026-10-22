using Microsoft.OpenApi;

namespace TacticusPlanner.Api.Http;

/// <summary>
/// Rewrites the pipeline's nullable reference shapes, <c>{ type: [null, object], oneOf: [{ $ref }] }</c> and the
/// bare <c>{ oneOf: [{ $ref }] }</c>, as
/// <c>{ oneOf: [{ type: null }, { $ref }] }</c>. Generators read the former as <c>(null | object) &amp; Ref</c>,
/// which drops the null; the latter is <c>null | Ref</c> (spec: openapi-contract).
/// </summary>
public static class NullableReferenceOpenApi
{
    public static void Apply(OpenApiDocument document)
    {
        foreach (var schema in document.Components?.Schemas?.Values ?? [])
            Visit(schema);

        foreach (var operation in (document.Paths ?? []).Values.SelectMany(path => path.Operations?.Values.AsEnumerable() ?? []))
        {
            foreach (var parameter in operation.Parameters ?? [])
                Visit(parameter.Schema);
            foreach (var media in operation.RequestBody?.Content?.Values ?? [])
                Visit(media.Schema);
            foreach (var media in (operation.Responses ?? []).Values.SelectMany(response => response.Content?.Values.AsEnumerable() ?? []))
                Visit(media.Schema);
        }
    }

    private static void Visit(IOpenApiSchema? candidate)
    {
        // References are visited through the component they point at.
        if (candidate is not OpenApiSchema schema)
            return;

        // The pipeline writes a nullable reference either with a null-including `type` next to a one-entry
        // oneOf, or (for some request records) as the bare one-entry oneOf; a non-null reference is a plain $ref.
        if (schema.OneOf is [OpenApiSchemaReference only]
            && (schema.Type is null || schema.Type.Value.HasFlag(JsonSchemaType.Null)))
        {
            schema.Type = null;
            schema.OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, only];
        }

        foreach (var property in schema.Properties?.Values ?? [])
            Visit(property);
        Visit(schema.Items);
        Visit(schema.AdditionalProperties);
        foreach (var part in (schema.OneOf ?? []).Concat(schema.AnyOf ?? []).Concat(schema.AllOf ?? []))
            Visit(part);
    }
}
