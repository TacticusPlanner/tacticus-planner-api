using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.Guilds;

/// <summary>
/// One Tacticus player ever seen on a guild's roster or in its raid hits. Unlike <see cref="GuildMember"/> it outlasts
/// roster membership, so a leader's name for a player stays when they leave.
/// </summary>
public class GuildPlayer : BaseEntity<GuildPlayerId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public GuildId GuildId { get; set; }

    /// <summary>Keyed hash of the player's Tacticus user id, the same as on <see cref="GuildMember"/> and on hits.</summary>
    public TacticusUserIdHash TacticusUserIdHash { get; set; }

    /// <summary>Last four characters of the Tacticus user id; <see langword="null"/> until a roster or hit shows them.</summary>
    public string? TacticusUserIdSuffix { get; set; }

    /// <summary>The name a verified leader gave the player; <see langword="null"/> when none.</summary>
    public string? AssignedName { get; set; }

    public virtual Guild? Guild { get; set; }
}
