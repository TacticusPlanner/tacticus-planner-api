using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.RosterSnapshots;

public sealed class RosterSnapshotOptions
{
    public const string SectionName = "RosterSnapshots";

    /// <summary>How many snapshots that count toward the limit (manual and V1 imports) a profile may hold.</summary>
    [Range(1, 1000)]
    public int PersonalLimit { get; set; } = PersonalRosterSnapshotValidation.DefaultLimit;
}

public abstract record CreatePersonalRosterSnapshotResult
{
    public sealed record Created(PersonalRosterSnapshot Snapshot) : CreatePersonalRosterSnapshotResult;

    public sealed record LimitReached(int Limit, int Count) : CreatePersonalRosterSnapshotResult;

    /// <summary>The profile has no Tacticus API key, so there is nothing to sync.</summary>
    public sealed record NoApiKey : CreatePersonalRosterSnapshotResult;

    /// <summary><c>fromLastSync</c> was asked for but the profile has never synced.</summary>
    public sealed record NotSynced : CreatePersonalRosterSnapshotResult;

    public sealed record UpstreamRejected : CreatePersonalRosterSnapshotResult;

    public sealed record UpstreamUnavailable : CreatePersonalRosterSnapshotResult;
}

/// <summary>
/// Takes personal roster snapshots (<c>personal-roster-snapshots</c>): from a fresh sync, captured inside the
/// sync's write transaction, or with <c>fromLastSync</c> from the stored roster without calling upstream. The limit
/// is checked before any upstream call, and again under the profile lock in the transaction that inserts the row,
/// so concurrent creates cannot exceed it.
/// </summary>
public sealed class PersonalRosterSnapshotService(
    PlannerDbContext db,
    PlayerDataSyncService syncService,
    IOptions<RosterSnapshotOptions> options)
{
    public int Limit => options.Value.PersonalLimit;

    /// <summary>The number of the caller's snapshots that count toward the limit.</summary>
    public Task<int> CountAsync(CancellationToken ct) =>
        db.PersonalRosterSnapshots.CountAsync(entity => entity.Source != RosterSnapshotSource.Auto, ct);

    public async Task<CreatePersonalRosterSnapshotResult> CreateAsync(
        ProfileId profileId, string? name, bool fromLastSync, CancellationToken ct)
    {
        var count = await CountAsync(ct);
        if (count >= Limit)
            return new CreatePersonalRosterSnapshotResult.LimitReached(Limit, count);

        return fromLastSync
            ? await CreateFromLastSyncAsync(profileId, name, ct)
            : await CreateFromSyncAsync(profileId, name, ct);
    }

    private async Task<CreatePersonalRosterSnapshotResult> CreateFromSyncAsync(
        ProfileId profileId, string? name, CancellationToken ct)
    {
        // TacticusIntegration is keyed 1:1 by the profile id, as PlayerSyncEndpoint reads it.
        var apiKey = await db.TacticusIntegrations
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.TacticusApiKey)
            .FirstOrDefaultAsync(ct);
        if (apiKey is null)
            return new CreatePersonalRosterSnapshotResult.NoApiKey();

        var capture = new CaptureStep(this, name);
        var result = await syncService.SyncAsync(profileId, apiKey, capture, ct);
        return result switch
        {
            PlayerDataSyncResult.UpstreamUnavailable => new CreatePersonalRosterSnapshotResult.UpstreamUnavailable(),
            PlayerDataSyncResult.UpstreamRejected => new CreatePersonalRosterSnapshotResult.UpstreamRejected(),
            _ => capture.Outcome ?? throw new InvalidOperationException("The sync committed without running the capture."),
        };
    }

    private async Task<CreatePersonalRosterSnapshotResult> CreateFromLastSyncAsync(
        ProfileId profileId, string? name, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var stored = await db.PlayerDataSnapshots.AsNoTracking()
                .Where(entity => entity.Id == profileId)
                .Select(entity => new { entity.SyncedAt, entity.Characters, entity.Mows })
                .FirstOrDefaultAsync(ct);
            if (stored is null)
                return new CreatePersonalRosterSnapshotResult.NotSynced();

            var outcome = await StageAsync(
                profileId, name, RosterSnapshotFactory.Create(stored.Characters, stored.Mows, stored.SyncedAt), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return outcome;
        });
    }

    /// <summary>Re-checks the limit under the profile lock and, when there is room, stages the snapshot. Must run
    /// inside a transaction.</summary>
    private async Task<CreatePersonalRosterSnapshotResult> StageAsync(
        ProfileId profileId, string? name, Domain.RosterSnapshots.RosterSnapshot roster, CancellationToken ct)
    {
        await db.LockCurrentProfileAsync(ct);
        var count = await CountAsync(ct);
        if (count >= Limit)
            return new CreatePersonalRosterSnapshotResult.LimitReached(Limit, count);

        var snapshot = PersonalRosterSnapshot.Create(profileId, name, RosterSnapshotSource.Manual, roster);
        db.PersonalRosterSnapshots.Add(snapshot);
        return new CreatePersonalRosterSnapshotResult.Created(snapshot);
    }

    /// <summary>Drops a staged snapshot (and its owned roster) after a failed save.</summary>
    internal void DiscardStaged()
    {
        var root = typeof(PersonalRosterSnapshot).FullName!;
        var staged = db.ChangeTracker.Entries()
            .Where(entry => entry.Metadata.Name == root
                || entry.Metadata.Name.StartsWith(root + ".", StringComparison.Ordinal))
            .ToList();
        foreach (var entry in staged)
            entry.State = EntityState.Detached;
    }

    /// <summary>Copies the roster the sync is writing. When the limit was reached meanwhile, the sync still commits
    /// and no snapshot is created.</summary>
    private sealed class CaptureStep(PersonalRosterSnapshotService owner, string? name) : IPlayerDataSyncPostWriteStep
    {
        public CreatePersonalRosterSnapshotResult? Outcome { get; private set; }

        public async Task ApplyAsync(PlayerDataSyncWrite write, CancellationToken ct) =>
            Outcome = await owner.StageAsync(
                write.ProfileId, name, RosterSnapshotFactory.Create(write.Characters, write.Mows, write.SyncedAt), ct);

        public void Discard()
        {
            Outcome = null;
            owner.DiscardStaged();
        }
    }
}
