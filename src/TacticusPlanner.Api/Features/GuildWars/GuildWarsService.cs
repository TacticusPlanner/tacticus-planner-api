using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TacticusPlanner.Domain.GuildWars;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.GuildWars;

/// <summary>
/// Shared war logic (<c>guild-war-offense</c>): the rule facts of the current catalog, the current war, and writes
/// under the profile lock, so the one-current-war index, the token count and the deployed units hold under
/// concurrency.
/// </summary>
public sealed class GuildWarsService(PlannerDbContext db, IGameCatalogProvider catalog)
{
    public const string RevisionStale = "guildWarRevisionStale";
    public const string AttackRevisionStale = "guildWarAttackRevisionStale";
    public const string NotStarted = "guildWarNotStarted";
    public const string HasAttacks = "guildWarHasAttacks";

    public GuildWarRuleFacts Facts => GuildWarCatalogFacts.For(catalog.Current);

    /// <summary>The current war with its attacks, or null; scoped by the query filter.</summary>
    public Task<GuildWar?> CurrentAsync(bool tracking, CancellationToken ct)
    {
        var wars = tracking ? db.GuildWars : db.GuildWars.AsNoTracking();
        return wars.Include(war => war.Attacks).FirstOrDefaultAsync(war => war.EndedAt == null, ct);
    }

    /// <summary>The war <paramref name="warId"/> with its attacks, tracked, or null; scoped by the query filter.</summary>
    public Task<GuildWar?> FindTrackedAsync(GuildWarId warId, CancellationToken ct) =>
        db.GuildWars.Include(war => war.Attacks).FirstOrDefaultAsync(war => war.Id == warId, ct);

    public Task<T> ExecuteLockedAsync<T>(Func<IDbContextTransaction, Task<T>> mutation, CancellationToken ct) =>
        db.ExecuteProfileLockedAsync(mutation, ct);

    /// <summary><paramref name="value"/> in UTC at the microsecond precision PostgreSQL stores, so a write's response
    /// reads the same as a later GET (and as the cursor of a page).</summary>
    public static DateTimeOffset Stored(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    /// <summary>The <c>issueCode</c> of a broken attack rule.</summary>
    public static string IssueCode(GuildWarAttackRuleCode code) => code switch
    {
        GuildWarAttackRuleCode.UnknownZone => "guildWarUnknownZone",
        GuildWarAttackRuleCode.UnknownUnit => "guildWarUnknownUnit",
        GuildWarAttackRuleCode.UnitDeployed => "guildWarUnitDeployed",
        GuildWarAttackRuleCode.TokensSpent => "guildWarTokensSpent",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
