using Microsoft.Extensions.DependencyInjection;

namespace TacticusPlanner.GameCatalog;

public static class GameCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddGameCatalog(this IServiceCollection services)
    {
        services.AddSingleton<IGameCatalogProvider, EmbeddedGameCatalogProvider>();

        return services;
    }
}
