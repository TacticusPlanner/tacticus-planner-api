namespace TacticusPlanner.Catalog;

public interface ICatalogProvider
{
    CatalogSnapshot Current { get; }
}
