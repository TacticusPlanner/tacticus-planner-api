using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Model;

namespace TacticusPlanner.Persistence.Users.Guilds;

/// <summary>
/// A registered Tacticus guild. The Guild API token used to (re-)synchronize membership is stored
/// directly on this row (encrypted) rather than in a separate credential table — Phase 1 keeps the
/// aggregate to two tables (<see cref="Guild"/> and <see cref="GuildMember"/>).
/// </summary>
public class Guild : BaseEntity<GuildId>, IRevisionedEntity
{
    public long Revision { get; set; }

    /// <summary>The upstream Tacticus guild id, encrypted at rest. Stored as the canonical Guid string
    /// so it can go through the same <see cref="EncryptedAttribute"/> converter as the other encrypted
    /// columns; <see cref="TacticusGuildIdHash"/> provides uniqueness/lookup without decrypting it.</summary>
    [Encrypted]
    public required string TacticusGuildId { get; set; }

    /// <summary>HMAC hash of <see cref="TacticusGuildId"/>, computed with the same keyed hash service as
    /// <c>Profile.TacticusUserIdHash</c> — enforces uniqueness and enables lookup-by-guild-id without
    /// decrypting <see cref="TacticusGuildId"/>.</summary>
    public byte[]? TacticusGuildIdHash { get; set; }

    public required string Tag { get; set; }

    public required string Name { get; set; }

    public int Level { get; set; }

    /// <summary>The Guild-scoped API token last used to register/synchronize this guild. Never included
    /// in any API projection, OpenAPI response type, or log line.</summary>
    [Encrypted]
    public string? GuildApiToken { get; set; }

    /// <summary>The profile that most recently registered or re-registered <see cref="GuildApiToken"/>.</summary>
    public ProfileId? ConfiguredByProfileId { get; set; }

    public DateTimeOffset? LastSyncAttemptedAt { get; set; }

    public DateTimeOffset? LastSyncSucceededAt { get; set; }

    public virtual Profile? ConfiguredByProfile { get; set; }

    public virtual ICollection<GuildMember> Members { get; set; } = new List<GuildMember>();
}
