namespace TacticusPlanner.Api.Persistence.Encryption;

public interface IColumnEncryptionService
{
    string? Encrypt(string? plaintext);

    string? Decrypt(string? envelope);
}
