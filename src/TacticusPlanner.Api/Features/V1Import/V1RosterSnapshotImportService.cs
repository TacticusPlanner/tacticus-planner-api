using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.RosterSnapshots;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// The <c>rosterSnapshots</c> part of the V1 import (<c>v1-roster-snapshot-import</c>): rebuilds every V1 snapshot
/// from its diff chain (<see cref="V1RosterSnapshotChain"/>), maps each live one onto the V2 roster shape, and stores
/// it as a <see cref="RosterSnapshotSource.V1Import"/> personal snapshot, newest first, within the room the manual
/// limit leaves. Each carries an immutable import key, so a re-run skips what an earlier run imported. Every source
/// snapshot gets exactly one outcome.
/// </summary>
public sealed partial class V1RosterSnapshotImportService(
    PlannerDbContext db,
    IGameCatalogProvider catalogProvider,
    IOptions<RosterSnapshotOptions> options,
    ILogger<V1RosterSnapshotImportService> logger)
{
    /// <summary>The catalog version every imported roster carries: its values are reconstructed, not synced.</summary>
    public const string CatalogVersion = "v1-import";

    private static readonly string[] Rarities = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];

    private static readonly string[] Stars =
    [
        "None", "OneStar", "TwoStars", "ThreeStars", "FourStars", "FiveStars", "RedOneStar",
        "RedTwoStars", "RedThreeStars", "RedFourStars", "RedFiveStars", "OneBlueStar",
        "TwoBlueStars", "ThreeBlueStars", "MythicWings",
    ];

    public async Task<V1RosterSnapshotImportResult> ImportAsync(
        ProfileId profileId, V1RosterSnapshotImportData source, CancellationToken ct)
    {
        if (!source.IsPresent)
        {
            return new V1RosterSnapshotImportResult(
                new ImportPartResult(ImportPartStatus.Skipped, "missing_roster_snapshots", "The V1 profile has no roster snapshots."),
                []);
        }

        if (source.State is null)
        {
            return new V1RosterSnapshotImportResult(
                new ImportPartResult(ImportPartStatus.Failed, "invalid_roster_snapshots", "The V1 roster snapshots could not be read."),
                []);
        }

        var mapper = new UnitMapper(catalogProvider.Current);
        var candidates = V1RosterSnapshotChain.Resolve(source.State)
            .OrderByDescending(snapshot => snapshot.DateMillisUtc)
            .Select(snapshot => Map(mapper, snapshot))
            .ToList();

        try
        {
            var outcomes = await StoreAsync(profileId, candidates, ct);
            return new V1RosterSnapshotImportResult(PartResult(outcomes), outcomes);
        }
        catch (DbUpdateException exception)
        {
            ImportFailed(logger, exception);
            return new V1RosterSnapshotImportResult(
                new ImportPartResult(ImportPartStatus.Failed, "roster_snapshot_import_conflict", "The roster snapshots changed during import. Try again."),
                []);
        }
    }

    private static ImportPartResult PartResult(List<V1RosterSnapshotOutcome> outcomes)
    {
        if (outcomes.Any(outcome => outcome.Status == V1RosterSnapshotOutcomeStatus.Imported))
            return new ImportPartResult(ImportPartStatus.Imported, null, null);
        if (outcomes.Count > 0 && outcomes.All(outcome => outcome.Status == V1RosterSnapshotOutcomeStatus.Failed))
            return new ImportPartResult(ImportPartStatus.Failed, "roster_snapshot_import_failed", "No V1 roster snapshot could be imported.");
        return new ImportPartResult(ImportPartStatus.Skipped, "no_roster_snapshot_imported", "No V1 roster snapshot was imported.");
    }

    /// <summary>Applies duplicates and the room under the limit, then stores the rest, all under the profile lock in
    /// one transaction.</summary>
    private async Task<List<V1RosterSnapshotOutcome>> StoreAsync(
        ProfileId profileId, List<Candidate> candidates, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async Task<List<V1RosterSnapshotOutcome>> () =>
        {
            var staged = new List<PersonalRosterSnapshot>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await db.LockCurrentProfileAsync(ct);
                var keys = candidates.Select(candidate => candidate.ImportKey).ToList();
                var existing = (await db.PersonalRosterSnapshots
                    .Where(entity => entity.ProfileId == profileId && entity.ImportKey != null && keys.Contains(entity.ImportKey))
                    .Select(entity => entity.ImportKey!)
                    .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
                var count = await db.PersonalRosterSnapshots
                    .CountAsync(entity => entity.ProfileId == profileId && entity.Source != RosterSnapshotSource.Auto, ct);
                var room = Math.Max(0, options.Value.PersonalLimit - count);

                var outcomes = new List<V1RosterSnapshotOutcome>();
                foreach (var candidate in candidates)
                {
                    var status = candidate switch
                    {
                        { Deleted: true } => V1RosterSnapshotOutcomeStatus.SkippedDeleted,
                        { Roster: null } => V1RosterSnapshotOutcomeStatus.Failed,
                        _ when existing.Contains(candidate.ImportKey) => V1RosterSnapshotOutcomeStatus.SkippedDuplicate,
                        _ when room == 0 => V1RosterSnapshotOutcomeStatus.SkippedLimit,
                        _ => V1RosterSnapshotOutcomeStatus.Imported,
                    };
                    if (status == V1RosterSnapshotOutcomeStatus.Imported)
                    {
                        var snapshot = PersonalRosterSnapshot.Create(
                            profileId, candidate.Name, RosterSnapshotSource.V1Import, candidate.Roster!, candidate.ImportKey);
                        db.PersonalRosterSnapshots.Add(snapshot);
                        staged.Add(snapshot);
                        existing.Add(candidate.ImportKey);
                        room--;
                    }

                    outcomes.Add(new V1RosterSnapshotOutcome(
                        candidate.SourceName,
                        candidate.TakenAt,
                        status,
                        Code(status),
                        candidate.DroppedUnits));
                }

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return outcomes;
            }
            catch
            {
                foreach (var snapshot in staged)
                    db.Entry(snapshot).State = EntityState.Detached;
                throw;
            }
        });
    }

    private static string? Code(V1RosterSnapshotOutcomeStatus status) => status switch
    {
        V1RosterSnapshotOutcomeStatus.SkippedDeleted => "v1_snapshot_deleted",
        V1RosterSnapshotOutcomeStatus.SkippedDuplicate => "v1_snapshot_already_imported",
        V1RosterSnapshotOutcomeStatus.SkippedLimit => "roster_snapshot_limit_reached",
        V1RosterSnapshotOutcomeStatus.Failed => "v1_snapshot_unreadable",
        _ => null,
    };

    /// <summary>Maps one rebuilt V1 snapshot. A failure leaves <see cref="Candidate.Roster"/> null, so only that
    /// snapshot fails.</summary>
    private Candidate Map(UnitMapper mapper, V1ResolvedSnapshot snapshot)
    {
        var sourceName = snapshot.Name;
        var takenAt = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.DateMillisUtc);
        var name = Truncate(sourceName.Trim(), PersonalRosterSnapshotValidation.MaxNameLength).TrimEnd();
        var importKey = Truncate(
            $"v1:{snapshot.DateMillisUtc.ToString(CultureInfo.InvariantCulture)}:{sourceName}",
            PersonalRosterSnapshotValidation.MaxImportKeyLength);
        if (snapshot.Deleted)
            return new Candidate(sourceName, name, takenAt, importKey, true, null, 0);

        try
        {
            var dropped = 0;
            var characters = snapshot.Characters
                .Select(unit => mapper.Character(unit, ref dropped))
                .OfType<PlayerCharacterRecord>()
                .OrderBy(unit => unit.UnitId.Value, StringComparer.Ordinal)
                .ToList();
            var mows = snapshot.Mows
                .Select(unit => mapper.Mow(unit, ref dropped))
                .OfType<PlayerMowRecord>()
                .OrderBy(unit => unit.UnitId.Value, StringComparer.Ordinal)
                .ToList();
            var roster = new RosterSnapshot
            {
                TakenAt = takenAt,
                CatalogVersion = CatalogVersion,
                Characters = characters,
                Mows = mows,
            };
            return new Candidate(sourceName, name, takenAt, importKey, false, roster, dropped);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            SnapshotFailed(logger, exception);
            return new Candidate(sourceName, name, takenAt, importKey, false, null, 0);
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    [LoggerMessage(Level = LogLevel.Warning, Message = "A V1 roster snapshot could not be mapped; it was not imported.")]
    private static partial void SnapshotFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The V1 roster snapshot import could not be saved.")]
    private static partial void ImportFailed(ILogger logger, Exception exception);

    private sealed record Candidate(
        string SourceName,
        string Name,
        DateTimeOffset TakenAt,
        string ImportKey,
        bool Deleted,
        RosterSnapshot? Roster,
        int DroppedUnits);

    /// <summary>Maps V1 units onto catalog ids and the V2 roster shape. Locked units are omitted; units and items the
    /// catalog does not know are omitted and counted.</summary>
    private sealed class UnitMapper(GameCatalogSnapshot catalog)
    {
        private readonly Dictionary<string, GameCatalogCharacter> characters =
            catalog.Characters.ToDictionary(unit => unit.Id, StringComparer.Ordinal);

        private readonly Dictionary<string, GameCatalogMow> mows =
            catalog.Mows.ToDictionary(unit => unit.Id, StringComparer.Ordinal);

        private readonly Dictionary<string, string> equipmentTypes = catalog.Equipment
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Type, StringComparer.Ordinal);

        public PlayerCharacterRecord? Character(V1ResolvedCharacter source, ref int dropped)
        {
            // V1 Rank 0 is Locked; V1 Stone1 (1) is V2 Stone1 (0).
            if (source.Rank == 0)
                return null;
            if (!characters.TryGetValue(source.Id, out var catalogUnit)
                || !Enum.IsDefined((UnitRank)(source.Rank - 1)))
            {
                dropped++;
                return null;
            }

            var unit = V1RosterSnapshotChain.Fix(source, RarityIndex(catalogUnit.InitialRarity));
            if (Progression(unit.Rarity, unit.Stars) is not { } progression)
            {
                dropped++;
                return null;
            }

            var abilities = new List<PlayerUnitAbilityRecord>();
            if (catalogUnit.ActiveAbilityNames is [var active, ..])
                abilities.Add(new PlayerUnitAbilityRecord { AbilityId = active, Level = unit.ActiveAbilityLevel });
            if (catalogUnit.PassiveAbilityNames is [var passive, ..])
                abilities.Add(new PlayerUnitAbilityRecord { AbilityId = passive, Level = unit.PassiveAbilityLevel });

            var equipped = new List<PlayerUnitEquipmentSlotRecord>();
            foreach (var (equipmentId, level) in unit.Equipment)
            {
                if (equipmentId is null)
                    continue;
                var slot = equipmentTypes.TryGetValue(equipmentId, out var type)
                    ? IndexOf(catalogUnit.EquipmentSlots, type)
                    : -1;
                var slotId = $"Slot{(slot + 1).ToString(CultureInfo.InvariantCulture)}";
                if (slot < 0 || equipped.Any(item => item.SlotId == slotId))
                {
                    dropped++;
                    continue;
                }

                equipped.Add(new PlayerUnitEquipmentSlotRecord
                {
                    SlotId = slotId,
                    EquipmentId = equipmentId,
                    Level = Math.Max(1, level ?? 1),
                });
            }

            return new PlayerCharacterRecord
            {
                UnitId = UnitId.From(unit.Id),
                ProgressionIndex = progression,
                Rank = (UnitRank)(unit.Rank - 1),
                Xp = 0,
                XpLevel = unit.XpLevel,
                Shards = unit.Shards,
                MythicShards = unit.MythicShards,
                Abilities = abilities,
                AppliedUpgradeSlots = [],
                EquippedItems = [.. equipped.OrderBy(item => item.SlotId, StringComparer.Ordinal)],
            };
        }

        public PlayerMowRecord? Mow(V1SnapshotMow source, ref int dropped)
        {
            if (source.Locked)
                return null;
            if (!mows.TryGetValue(source.Id, out var catalogUnit))
            {
                dropped++;
                return null;
            }

            // The catalog records no initial rarity for MoWs, so only the per-rarity star floor applies.
            var unit = V1RosterSnapshotChain.Fix(source, initialRarity: null);
            if (Progression(unit.Rarity, unit.Stars) is not { } progression)
            {
                dropped++;
                return null;
            }

            return new PlayerMowRecord
            {
                UnitId = UnitId.From(unit.Id),
                ProgressionIndex = progression,
                Xp = 0,
                XpLevel = 0,
                Shards = unit.Shards,
                MythicShards = unit.MythicShards,
                Abilities =
                [
                    new PlayerUnitAbilityRecord { AbilityId = catalogUnit.PrimaryAbility.Name, Level = unit.PrimaryAbilityLevel },
                    new PlayerUnitAbilityRecord { AbilityId = catalogUnit.SecondaryAbility.Name, Level = unit.SecondaryAbilityLevel },
                ],
                AppliedUpgradeSlots = [],
            };
        }

        /// <summary>V1 rarity and stars as a V2 progression. A pair that still does not exist after the floors (stars
        /// beyond the rarity) is clamped to the rarity's minimum stars; an unknown rarity is null.</summary>
        private static UnitProgression? Progression(int rarity, int stars)
        {
            if (rarity < 0 || rarity >= Rarities.Length)
                return null;
            var wire = stars >= 0 && stars < Stars.Length ? $"{Rarities[rarity]}:{Stars[stars]}" : null;
            if (wire is null || ProgressionRules.ProgressionIndex(wire) < 0)
                wire = $"{Rarities[rarity]}:{Stars[V1RosterSnapshotChain.MinimumStarsFor(rarity)]}";
            return ProgressionRules.FromWireName(wire);
        }

        private static int? RarityIndex(string rarity)
        {
            var index = Array.FindIndex(Rarities, name => string.Equals(name, rarity, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? null : index;
        }

        private static int IndexOf(IReadOnlyList<string> slots, string type)
        {
            for (var index = 0; index < slots.Count; index++)
            {
                if (string.Equals(slots[index], type, StringComparison.Ordinal))
                    return index;
            }

            return -1;
        }
    }
}

public sealed record V1RosterSnapshotImportResult(ImportPartResult Part, IReadOnlyList<V1RosterSnapshotOutcome> Outcomes);

/// <summary>One V1 roster snapshot's outcome: its V1 name and date, what happened to it, why (null when imported),
/// and how many unknown units and items were left out.</summary>
public sealed record V1RosterSnapshotOutcome(
    string Name,
    DateTimeOffset TakenAt,
    V1RosterSnapshotOutcomeStatus Status,
    string? Code,
    int DroppedUnits);

public enum V1RosterSnapshotOutcomeStatus
{
    [JsonStringEnumMemberName("imported")] Imported,
    [JsonStringEnumMemberName("skippedDeleted")] SkippedDeleted,
    [JsonStringEnumMemberName("skippedDuplicate")] SkippedDuplicate,
    [JsonStringEnumMemberName("skippedLimit")] SkippedLimit,
    [JsonStringEnumMemberName("failed")] Failed,
}
