using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.RosterSnapshots;

/// <summary>
/// Takes an automatic roster snapshot after a sync (<c>automatic-roster-snapshots</c>): when the profile's
/// <c>autoRosterSnapshots</c> setting is on and no roster snapshot of any kind (personal snapshots of every source and
/// Legendary Event run-record rosters, including one this sync just opened or refreshed) was taken within the window.
/// At the rolling quota the oldest automatic snapshot is deleted in the same unit of work; manual and imported
/// snapshots are never touched. It runs after the sync has saved its own writes, so its checks see this sync's run
/// records, and the sync isolates its failures (see <see cref="IPlayerDataSyncDefaultStep"/>).
/// </summary>
public sealed class AutoRosterSnapshotPolicy(
    PlannerDbContext db,
    IOptions<RosterSnapshotOptions> options) : IPlayerDataSyncDefaultStep
{
    public async Task ApplyAsync(PlayerDataSyncWrite write, CancellationToken ct)
    {
        var profileId = write.ProfileId;
        var enabled = await db.UserSettings.AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.Settings.AutoRosterSnapshots)
            .FirstOrDefaultAsync(ct);
        if (enabled == false)
            return;

        // Serializes with a concurrent sync or manual snapshot of the same profile.
        await db.LockCurrentProfileAsync(ct);
        var since = write.SyncedAt - TimeSpan.FromDays(options.Value.AutoWindowDays);
        // One round trip for both kinds of roster snapshot.
        var recent = await db.Profiles
            .Where(profile => profile.Id == profileId)
            .Select(_ =>
                db.PersonalRosterSnapshots.Any(entity => entity.ProfileId == profileId && entity.TakenAt > since)
                || db.LegendaryEventRunRecords.Any(entity => entity.ProfileId == profileId && entity.Roster.TakenAt > since))
            .FirstAsync(ct);
        if (recent)
            return;

        var autos = await db.PersonalRosterSnapshots
            .Where(entity => entity.ProfileId == profileId && entity.Source == RosterSnapshotSource.Auto)
            .OrderBy(entity => entity.TakenAt)
            .ThenBy(entity => entity.Id)
            .Select(entity => entity.Id)
            .ToListAsync(ct);
        var excess = autos.Count - options.Value.AutoLimit + 1;
        if (excess > 0)
        {
            var oldest = autos.Take(excess).ToList();
            await db.PersonalRosterSnapshots
                .Where(entity => oldest.Contains(entity.Id))
                .ExecuteDeleteAsync(ct);
        }

        db.PersonalRosterSnapshots.Add(PersonalRosterSnapshot.Create(
            profileId,
            name: null,
            RosterSnapshotSource.Auto,
            RosterSnapshotFactory.Create(write.Characters, write.Mows, write.SyncedAt)));
    }

    public void Discard()
    {
        var root = typeof(PersonalRosterSnapshot).FullName!;
        foreach (var entry in db.ChangeTracker.Entries()
                     .Where(entry => entry.Metadata.Name == root
                         || entry.Metadata.Name.StartsWith(root + ".", StringComparison.Ordinal))
                     .ToList())
            entry.State = EntityState.Detached;
    }
}
