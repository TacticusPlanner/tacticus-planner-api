namespace TacticusPlanner.Api.Persistence.Encryption;

public interface IColumnHashService
{
    byte[]? ComputeHash(string? value);
}
