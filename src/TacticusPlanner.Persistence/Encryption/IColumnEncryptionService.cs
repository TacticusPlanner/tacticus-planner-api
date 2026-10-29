namespace TacticusPlanner.Persistence.Encryption;

public interface IColumnEncryptionService
{
    /// <param name="column">Stable <c>table.column</c> name, bound into the ciphertext as associated data.</param>
    string? Encrypt(string? plaintext, string column);

    /// <param name="column">The same <c>table.column</c> name the value was encrypted for.</param>
    string? Decrypt(string? envelope, string column);
}
