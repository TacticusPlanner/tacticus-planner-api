using TacticusPlanner.Persistence.Model;

namespace TacticusPlanner.Persistence.Users.Guilds;

/// <summary>
/// One upstream guild-roster row, kept in sync with the authoritative Tacticus guild response. Rows for
/// members absent from the latest successful synchronization are deleted, not retained — see the Guild
/// Phase 1 spec's failure/consistency rules.
/// </summary>
public class GuildMember : BaseEntity<GuildMemberId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public GuildId GuildId { get; set; }

    /// <summary>The upstream Tacticus user id. Not encrypted at rest (it identifies a guild-roster
    /// position, not a credential), but always masked before it leaves the server in any API response —
    /// see <c>SecretMasker</c> in the API project.</summary>
    public Guid TacticusUserId { get; set; }

    /// <summary>HMAC hash of <see cref="TacticusUserId"/>, computed with the same keyed hash service as
    /// <c>Profile.TacticusUserIdHash</c>, so the two can be matched without decrypting either side.</summary>
    public byte[]? TacticusUserIdHash { get; set; }

    /// <summary>The Planner profile this member is linked to, re-evaluated on every synchronization. Null
    /// when no configured profile matches this member's Tacticus user id yet.</summary>
    public ProfileId? ProfileId { get; set; }

    public GuildRole Role { get; set; }

    public int Level { get; set; }

    public long? LastActivityOn { get; set; }

    /// <summary>Display name resolved at sync time from the linked profile's player-data snapshot name,
    /// falling back to the profile's display name. Null when unlinked — the upstream guild response does
    /// not contain member names, so an unlinked member has no name to show. Kept separate from the future
    /// member-name override field (not implemented in Phase 1).</summary>
    public string? LinkedPlayerName { get; set; }

    public DateTimeOffset LastSyncedAt { get; set; }

    public virtual Guild? Guild { get; set; }

    public virtual Profile? Profile { get; set; }
}
