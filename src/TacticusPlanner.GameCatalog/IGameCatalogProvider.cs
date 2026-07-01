using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

public interface IGameCatalogProvider
{
    GameCatalogSnapshot Current { get; }
}
