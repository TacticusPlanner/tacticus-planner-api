using System.IO.Compression;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace TacticusPlanner.ServiceDefaults;

public static class Extensions
{
    private const string LivenessTag = "live";

    // Readiness runs every check (a database round-trip included) and is anonymous, so its result is shared
    // across callers for a few seconds; liveness stays uncached and cheap.
    private static readonly TimeSpan ReadinessCacheDuration = TimeSpan.FromSeconds(5);

    public static IHostApplicationBuilder AddServiceDefaults(
        this IHostApplicationBuilder builder
    )
    {
        builder.ConfigureOpenTelemetry();

        // Brotli is preferred over gzip; HTTPS is safe because responses carry no attacker-reflected secrets.
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = [.. ResponseCompressionDefaults.MimeTypes, "application/problem+json"];
        });

        // The default "Fastest" Brotli level compresses the catalog datasets worse than gzip does (1.1 MB vs 0.66 MB
        // for the whole first visit); Optimal gets it to ~0.35 MB at negligible CPU cost, SmallestSize costs seconds.
        builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
        builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);

        builder.Services.AddServiceDiscovery();
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LivenessTag]);
        builder.Services.AddOutputCache();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retrying a POST/PUT/PATCH/DELETE after a timeout or 5xx can apply it upstream twice; only safe
            // methods (GET, HEAD, OPTIONS, TRACE) are retried.
            http.AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Output caching is opt-in per endpoint; only /health/ready uses it.
        app.UseOutputCache();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LivenessTag),
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
        }).AllowAnonymous().CacheOutput(new ReadinessCachePolicy());

        return app;
    }

    /// <summary>Caches the readiness response, healthy or not, for <see cref="ReadinessCacheDuration"/> under one
    /// key. Unlike the default output-cache policy it ignores the query string, Host and Authorization headers,
    /// so a caller cannot bypass the cache to force a fresh database round-trip.</summary>
    private sealed class ReadinessCachePolicy : IOutputCachePolicy
    {
        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            context.EnableOutputCaching = true;
            context.AllowCacheLookup = true;
            context.AllowCacheStorage = true;
            context.AllowLocking = true;
            context.ResponseExpirationTimeSpan = ReadinessCacheDuration;
            context.CacheVaryByRules.VaryByHost = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) =>
            ValueTask.CompletedTask;

        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation) =>
            ValueTask.CompletedTask;
    }

    private static void ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
        );

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;

            if (useOtlpExporter)
            {
                logging.AddOtlpExporter();
            }
        });

        var telemetry = builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (useOtlpExporter)
                {
                    metrics.AddOtlpExporter();
                }
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();

                if (useOtlpExporter)
                {
                    tracing.AddOtlpExporter();
                }
            });

        var applicationInsightsConnectionString =
            builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

        if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
        {
            telemetry.UseAzureMonitor(options =>
            {
                options.ConnectionString = applicationInsightsConnectionString;
            });
        }
    }
}
