namespace TacticusPlanner.Api.Features.Catalog;

internal sealed class CatalogQuery
{
    private readonly IReadOnlyDictionary<string, string> filters;

    private CatalogQuery(IReadOnlyDictionary<string, string> filters)
    {
        this.filters = filters;
    }

    public bool HasFilters => filters.Count > 0;

    public IEnumerable<KeyValuePair<string, string?>> NormalizedFilters => filters
        .Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value));

    public static CatalogQuery From(HttpContext httpContext)
    {
        var filters = httpContext.Request.Query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.ToString()))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase
            );

        return new CatalogQuery(filters);
    }

    public bool TryGet(string key, out string value) => filters.TryGetValue(key, out value!);

    public IEnumerable<T> ApplySearch<T>(
        IEnumerable<T> items,
        Func<T, IEnumerable<string?>> searchableValues
    )
    {
        if (!TryGet("search", out var search))
        {
            return items;
        }

        return items.Where(item => searchableValues(item).Any(value =>
            value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        ));
    }

    public IEnumerable<T> ApplyEquals<T>(
        IEnumerable<T> items,
        string key,
        Func<T, string?> valueSelector
    )
    {
        if (!TryGet(key, out var expected))
        {
            return items;
        }

        return items.Where(item => string.Equals(valueSelector(item), expected, StringComparison.OrdinalIgnoreCase));
    }
}
