using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;
using Vogen;

namespace TacticusPlanner.Domain.GuildWars;

[ValueObject<Guid>]
public readonly partial struct GuildWarId : IGuidValueObject;

[ValueObject<Guid>]
public readonly partial struct GuildWarAttackId : IGuidValueObject;

/// <summary>
/// One Guild War as a profile played it (<c>guild-war-offense</c>): the season and battlefield level the player picked
/// and the attacks they logged. A profile has at most one current war (<see cref="EndedAt"/> null); starting the next
/// ends it and keeps it as history (design D4). Tokens used and deployed units are derived from the attacks (D7).
/// </summary>
public class GuildWar : BaseEntity<GuildWarId>, IRevisionedEntity
{
    /// <summary>Optimistic-concurrency counter for the season and level; attacks carry their own.</summary>
    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    /// <summary>The <c>guild-war</c> season id the player picked, e.g. <c>guildWarSeasonA</c> (design D5).</summary>
    public string SeasonId { get; set; } = string.Empty;

    /// <summary>The battlefield level the player picked, 0 to 5.</summary>
    public int BattlefieldLevel { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the next war was started; null for the current war.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public List<GuildWarAttack> Attacks { get; set; } = [];

    public virtual Profile? Profile { get; set; }

    public static GuildWar Start(ProfileId profileId, string seasonId, int battlefieldLevel, DateTimeOffset now) => new()
    {
        Id = GuildWarId.From(Guid.CreateVersion7()),
        ProfileId = profileId,
        SeasonId = seasonId,
        BattlefieldLevel = battlefieldLevel,
        StartedAt = now,
    };

    /// <summary>The attacks oldest first, the id as the tiebreak: the order every read serves.</summary>
    public IEnumerable<GuildWarAttack> OrderedAttacks() =>
        Attacks.OrderBy(attack => attack.AttackedAt).ThenBy(attack => attack.Id.Value);

    /// <summary>Every unit of every attack, each attack's characters then its MoW, in attack order (design D7).</summary>
    public IReadOnlyList<string> DeployedUnitIds() =>
        [.. OrderedAttacks().SelectMany(attack => attack.Lineup.UnitIds())];
}

/// <summary>
/// One attack of a war: the target, the rarity cap the server derived from it (design D6), the lineup as the player had
/// it (D8), and an optional result and enemy details. Lineup and enemy are read and written whole (jsonb).
/// </summary>
public class GuildWarAttack : BaseEntity<GuildWarAttackId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public GuildWarId WarId { get; set; }

    /// <summary>The owning profile, denormalized from the war so the query filter needs no join.</summary>
    public ProfileId ProfileId { get; set; }

    public DateTimeOffset AttackedAt { get; set; }

    /// <summary>A zone id of the war's season.</summary>
    public string ZoneId { get; set; } = string.Empty;

    /// <summary>The defender slot, 0 to 4.</summary>
    public int Slot { get; set; }

    /// <summary>The slot's rarity cap at the war's battlefield level when the target was set.</summary>
    public GuildWarRarity RarityCap { get; set; }

    public GuildWarLineup Lineup { get; set; } = new();

    public GuildWarAttackResult? Result { get; set; }

    /// <summary>Units the player lost, 0 to 6; only with a result.</summary>
    public int? UnitsLost { get; set; }

    public GuildWarEnemy? Enemy { get; set; }

    public virtual GuildWar? War { get; set; }
}

/// <summary>The player's lineup: 1 to 5 characters and at most one MoW.</summary>
public sealed class GuildWarLineup
{
    public List<GuildWarLineupUnit> Characters { get; set; } = [];

    public GuildWarLineupUnit? Mow { get; set; }

    public IEnumerable<string> UnitIds() =>
        Mow is null ? Characters.Select(unit => unit.UnitId) : [.. Characters.Select(unit => unit.UnitId), Mow.UnitId];
}

/// <summary>A lineup unit as the player had it: progression, rank (null for a MoW), XP level and ability levels.</summary>
public sealed class GuildWarLineupUnit
{
    public string UnitId { get; set; } = string.Empty;

    public GameDomain.UnitProgression Progression { get; set; }

    public GameDomain.UnitRank? Rank { get; set; }

    public int XpLevel { get; set; }

    public List<int> AbilityLevels { get; set; } = [];
}

/// <summary>What the player saw on the other side: optional, every field optional.</summary>
public sealed class GuildWarEnemy
{
    public List<GuildWarEnemyUnit> Characters { get; set; } = [];

    public GuildWarEnemyUnit? Mow { get; set; }

    /// <summary>Buff ids of the war's season that were active.</summary>
    public List<string> BuffIds { get; set; } = [];

    /// <summary>A board id of the season's map pool.</summary>
    public string? BoardId { get; set; }
}

public sealed class GuildWarEnemyUnit
{
    public string UnitId { get; set; } = string.Empty;

    public GuildWarRarity? Rarity { get; set; }

    public GameDomain.UnitRank? Rank { get; set; }

    public GuildWarEnemyUnitState? State { get; set; }
}
