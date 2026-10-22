using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using TacticusPlanner.Domain.PlayerData.Chunks;

namespace TacticusPlanner.Api.Http;

/// <summary>
/// The synced player-data chunk records are settable classes, so the pipeline lists none of their properties as
/// required, yet every property is always written (no ignore conditions). Lists them all, so a generated client
/// does not see a served roster's fields as optional (spec: openapi-contract).
/// </summary>
public static class PlayerDataChunkOpenApi
{
    private static readonly string ChunkNamespace = typeof(PlayerCharacterRecord).Namespace!;

    public static Task Apply(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken _)
    {
        if (context.JsonTypeInfo.Type.Namespace == ChunkNamespace && schema.Properties is { Count: > 0 } properties)
            schema.Required = new HashSet<string>(properties.Keys);
        return Task.CompletedTask;
    }
}
