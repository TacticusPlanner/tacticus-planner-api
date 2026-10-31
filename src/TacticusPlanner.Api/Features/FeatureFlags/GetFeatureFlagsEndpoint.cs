using FastEndpoints;
using Microsoft.FeatureManagement;
using TacticusPlanner.Persistence.FeatureGates;

namespace TacticusPlanner.Api.Features.FeatureFlags;

public sealed class GetFeatureFlagsEndpoint(IFeatureManager featureManager, SchemaFeatureGate schemaFeatures)
    : EndpointWithoutRequest<FeatureFlagsResponse>
{
    public override void Configure()
    {
        Get("feature-flags");
        AllowAnonymous();
        Summary(summary =>
        {
            summary.Summary = "Lists the feature flags turned on in this environment.";
            summary.Description = "Returns only the names of enabled flags, so flags still off here are not "
                + "revealed. The client hides unfinished features whose flag is not listed.";
            summary.Response<FeatureFlagsResponse>(StatusCodes.Status200OK, "The enabled feature flags.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var enabled = new List<string>();
        await foreach (var name in featureManager.GetFeatureNamesAsync().WithCancellation(ct))
        {
            // A schema flag turned on since startup stays hidden until the restart that migrates and maps it.
            if (schemaFeatures.IsEnabled(name) && await featureManager.IsEnabledAsync(name, ct))
            {
                enabled.Add(name);
            }
        }

        enabled.Sort(StringComparer.Ordinal);

        // Short-lived: a toggled flag reaches clients within a minute of the API picking it up.
        HttpContext.Response.Headers.CacheControl = "public, max-age=60";

        await Send.OkAsync(new FeatureFlagsResponse(enabled), ct);
    }
}

/// <param name="Enabled">Names of the feature flags turned on, in ordinal order.</param>
public sealed record FeatureFlagsResponse(IReadOnlyList<string> Enabled);
