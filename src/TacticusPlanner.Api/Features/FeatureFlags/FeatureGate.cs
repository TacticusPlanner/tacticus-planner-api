using FastEndpoints;
using Microsoft.FeatureManagement;
using TacticusPlanner.Persistence.FeatureGates;

namespace TacticusPlanner.Api.Features.FeatureFlags;

public static class FeatureGate
{
    /// <summary>
    /// Answers 404 (the shared problem body, via status-code pages) while <paramref name="featureFlag"/> is off, as if
    /// the endpoint did not exist. Call from an endpoint's <c>Configure()</c>.
    /// </summary>
    /// <remarks>
    /// A schema flag (see <see cref="SchemaFeatureGate"/>) also needs to have been on when the API started, because
    /// its columns exist and are mapped only from then on.
    /// </remarks>
    public static void RequireFeature(this BaseEndpoint endpoint, string featureFlag) =>
        endpoint.Definition.Options(route => route.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var featureManager = services.GetRequiredService<IFeatureManager>();
            return services.GetRequiredService<SchemaFeatureGate>().IsEnabled(featureFlag)
                && await featureManager.IsEnabledAsync(featureFlag, context.HttpContext.RequestAborted)
                    ? await next(context)
                    : Results.NotFound();
        }));
}
