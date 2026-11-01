using Microsoft.Extensions.Options;

namespace TacticusPlanner.Api.Features.StagingTransfer;

public static class DependencyInjection
{
    /// <summary>A whole profile's bundle; generous, but bounded so a misbehaving source cannot exhaust memory.</summary>
    private const long MaxResponseContentBufferSize = 32 * 1024 * 1024;

    public static IServiceCollection AddStagingTransferFeature(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(StagingTransferOptions.SectionName);
        services.AddOptions<StagingTransferOptions>().Bind(section);
        services.AddSingleton<StagingTransferCodeService>();
        services.AddScoped<StagingTransferExporter>();
        services.AddScoped<StagingTransferImporter>();

        services.AddHttpClient(StagingTransferSourceClient.HttpClientName, (provider, client) =>
        {
            var sourceBaseUrl = provider.GetRequiredService<IOptions<StagingTransferOptions>>().Value.SourceBaseUrl;
            if (Uri.TryCreate(sourceBaseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(30);
            client.MaxResponseContentBufferSize = MaxResponseContentBufferSize;
        })
            // A redirect would replay the service-key header to wherever the Location points.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddScoped<IStagingTransferSourceClient, StagingTransferSourceClient>();

        return services;
    }
}
