using Microsoft.Extensions.DependencyInjection;

namespace TacticusPlanner.Catalog;

public static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddCatalog(this IServiceCollection services)
    {
        services.AddSingleton<ICatalogProvider, EmbeddedCatalogProvider>();

        return services;
    }
}
