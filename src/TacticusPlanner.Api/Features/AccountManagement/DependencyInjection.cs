using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace TacticusPlanner.Api.Features.AccountManagement;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountManagementFeature(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var options = configuration.GetSection(ExternalIdentityOptions.SectionName).Get<ExternalIdentityOptions>()
            ?? new ExternalIdentityOptions();

        if (!options.IsConfigured)
        {
            services.AddSingleton<IExternalIdentityDeleter, NoOpExternalIdentityDeleter>();
            return services;
        }

        services.AddSingleton(Options.Create(options));
        services.AddHttpClient(
            GraphExternalIdentityDeleter.HttpClientName,
            client => client.BaseAddress = new Uri("https://graph.microsoft.com/")
        );
        services.AddSingleton<IExternalIdentityDeleter>(provider => new GraphExternalIdentityDeleter(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(GraphExternalIdentityDeleter.HttpClientName),
            CreateCredential(options),
            options.PermanentlyDelete,
            provider.GetRequiredService<ILogger<GraphExternalIdentityDeleter>>()
        ));

        return services;
    }

    internal static TokenCredential CreateCredential(ExternalIdentityOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret);
        }

        // The managed identity lives in the hosting (workforce) tenant, so it cannot call the External ID tenant's
        // Graph itself; instead its token is the assertion for the app registration's federated identity credential.
        var managedIdentity = new ManagedIdentityCredential(
            string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId)
        );
        var exchangeScope = new TokenRequestContext(["api://AzureADTokenExchange/.default"]);

        return new ClientAssertionCredential(
            options.TenantId,
            options.ClientId,
            async cancellationToken =>
                (await managedIdentity.GetTokenAsync(exchangeScope, cancellationToken)).Token
        );
    }
}
