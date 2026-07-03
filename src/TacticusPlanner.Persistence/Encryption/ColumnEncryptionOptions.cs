namespace TacticusPlanner.Persistence.Encryption;

public sealed class ColumnEncryptionOptions
{
    public const string SectionName = "ColumnEncryption";

    public string? CurrentKeyVersion { get; set; }

    public Dictionary<string, string> Keys { get; set; } = [];
}
