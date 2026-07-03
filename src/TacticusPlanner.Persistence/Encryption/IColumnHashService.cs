namespace TacticusPlanner.Persistence.Encryption;

public interface IColumnHashService
{
    byte[]? ComputeHash(string? value);
}
