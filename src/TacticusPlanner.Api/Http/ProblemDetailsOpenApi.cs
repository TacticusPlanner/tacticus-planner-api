using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace TacticusPlanner.Api.Http;

/// <summary>Documents the single error shape (<see cref="ApiProblems"/>) on every 4xx/5xx response of the OpenAPI
/// document, so generated and hand-written clients read one schema.</summary>
public static class ProblemDetailsOpenApi
{
    public const string SchemaName = "ApiProblem";

    public static void Apply(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas[SchemaName] = Schema();

        var operations = (document.Paths ?? []).Values
            .SelectMany(path => path.Operations?.Values.AsEnumerable() ?? []);
        foreach (var operation in operations)
        {
            foreach (var (status, response) in operation.Responses ?? new OpenApiResponses())
            {
                if (!status.StartsWith('4') && !status.StartsWith('5') || response is not OpenApiResponse concrete)
                    continue;
                concrete.Content = new Dictionary<string, OpenApiMediaType>
                {
                    [ApiProblems.ContentType] = new() { Schema = new OpenApiSchemaReference(SchemaName, document) },
                };
            }
        }
    }

    private static OpenApiSchema Schema() => new()
    {
        Type = JsonSchemaType.Object,
        Description = "RFC 7807 problem details. issueCode names the failure; errors maps fields to messages on a "
            + "validation failure; current carries the conflicting resource in its read shape on a 409.",
        Required = new HashSet<string> { "type", "title", "status", "detail", "instance", "traceId", "issueCode" },
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["issueCode"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Examples = [JsonValue.Create("validation"), JsonValue.Create("goalRevisionStale")],
            },
            ["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                AdditionalProperties = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema { Type = JsonSchemaType.String },
                },
            },
            ["current"] = new OpenApiSchema { Description = "The conflicting resource as its read endpoint serves it." },
        },
    };
}
