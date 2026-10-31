using FastEndpoints;
using Microsoft.FeatureManagement;

namespace TacticusPlanner.Api.Features.FeatureFlags;

public static class FeatureGate
{
    /// <summary>
    /// Answers 404 (the shared problem body, via status-code pages) while <paramref name="featureFlag"/> is off, as if
    /// the endpoint did not exist. Call from an endpoint's <c>Configure()</c>.
    /// </summary>
    public static void RequireFeature(this BaseEndpoint endpoint, string featureFlag) =>
        endpoint.Definition.Options(route => route.AddEndpointFilter(async (context, next) =>
        {
            var featureManager = context.HttpContext.RequestServices.GetRequiredService<IFeatureManager>();
            return await featureManager.IsEnabledAsync(featureFlag, context.HttpContext.RequestAborted)
                ? await next(context)
                : Results.NotFound();
        }));
}
