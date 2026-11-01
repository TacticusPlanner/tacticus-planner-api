using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TacticusPlanner.GameCatalog;

public static class GameCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddGameCatalog(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IGameCatalogProvider, EmbeddedGameCatalogProvider>();

        return services;
    }
}
