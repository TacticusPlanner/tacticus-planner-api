using System.Globalization;
using Npgsql.NameTranslation;

namespace TacticusPlanner.Persistence.Configurations;

internal static class PostgresNaming
{
    public static string SnakeCase(string propertyName) =>
        NpgsqlSnakeCaseNameTranslator.ConvertToSnakeCase(propertyName, CultureInfo.InvariantCulture);
}
