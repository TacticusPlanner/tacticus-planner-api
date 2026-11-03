using System.Text.Json;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>Distinguishes a profile with no V1 roster snapshots from one whose snapshots cannot be read; the import
/// reports those as Skipped and Failed.</summary>
public sealed record V1RosterSnapshotImportData(bool IsPresent, V1RosterSnapshotsState? State)
{
    public static V1RosterSnapshotImportData Missing() => new(false, null);

    public static V1RosterSnapshotImportData Invalid() => new(true, null);

    public static V1RosterSnapshotImportData Valid(V1RosterSnapshotsState state) => new(true, state);
}

/// <summary>V1 <c>IRosterSnapshotsState</c>: <c>diffs[0]</c> is relative to <c>base</c>, and <c>diffs[i]</c> to
/// <c>diffs[i - 1]</c>.</summary>
public sealed record V1RosterSnapshotsState(V1RosterSnapshot? Base, IReadOnlyList<V1RosterSnapshotDiff>? Diffs);

/// <summary>V1 <c>IRosterSnapshot</c>. Dates are milliseconds since the epoch (UTC).</summary>
public sealed record V1RosterSnapshot(
    string? Name,
    long DateMillisUtc,
    long? DeletedDateMillisUtc,
    IReadOnlyList<V1SnapshotCharacter>? Chars,
    IReadOnlyList<V1SnapshotMow>? Mows);

/// <summary>V1 <c>ISnapshotCharacter</c>. Rank, rarity and stars are V1's numeric enums. A base snapshot stores each
/// <c>equipN</c> as V1's whole equipment object, a diff as its id; <see cref="V1RosterSnapshotChain.EquipmentId"/>
/// reads both.</summary>
public sealed record V1SnapshotCharacter(
    string Id,
    int Rank,
    int Rarity,
    int Stars,
    long Shards,
    long MythicShards,
    int ActiveAbilityLevel,
    int PassiveAbilityLevel,
    int XpLevel,
    JsonElement? Equip0 = null,
    JsonElement? Equip1 = null,
    JsonElement? Equip2 = null,
    int? Equip0Level = null,
    int? Equip1Level = null,
    int? Equip2Level = null);

/// <summary>V1 <c>ISnapshotMachineOfWar</c>.</summary>
public sealed record V1SnapshotMow(
    string Id,
    int Rarity,
    int Stars,
    int PrimaryAbilityLevel,
    int SecondaryAbilityLevel,
    long Shards,
    long MythicShards,
    bool Locked);

/// <summary>V1 <c>IRosterSnapshotDiff</c>.</summary>
public sealed record V1RosterSnapshotDiff(
    string? Name,
    long DateMillisUtc,
    long? DeletedDateMillisUtc,
    IReadOnlyList<V1SnapshotUnitDiff>? CharDiffs,
    IReadOnlyList<V1SnapshotUnitDiff>? MowDiffs);

/// <summary>V1 <c>ISnapshotUnitDiff</c>: only the fields that changed. For a MoW, <c>active</c>/<c>passive</c> are
/// its primary/secondary ability levels.</summary>
public sealed record V1SnapshotUnitDiff(
    string Id,
    int? Rank = null,
    int? Rarity = null,
    int? Stars = null,
    long? Shards = null,
    long? MythicShards = null,
    int? XpLevel = null,
    int? Active = null,
    int? Passive = null,
    bool? Locked = null,
    JsonElement? Equip0 = null,
    JsonElement? Equip1 = null,
    JsonElement? Equip2 = null,
    int? Equip0Level = null,
    int? Equip1Level = null,
    int? Equip2Level = null);

/// <summary>One V1 snapshot rebuilt in full, in V1's own shape, with its equipment reduced to ids.</summary>
public sealed record V1ResolvedSnapshot(
    string Name,
    long DateMillisUtc,
    bool Deleted,
    IReadOnlyList<V1ResolvedCharacter> Characters,
    IReadOnlyList<V1SnapshotMow> Mows);

public sealed record V1ResolvedCharacter(
    string Id,
    int Rank,
    int Rarity,
    int Stars,
    long Shards,
    long MythicShards,
    int ActiveAbilityLevel,
    int PassiveAbilityLevel,
    int XpLevel,
    IReadOnlyList<(string? EquipmentId, int? Level)> Equipment);

/// <summary>
/// A port of V1 <c>RosterSnapshotsService</c>'s chain rules (<c>v1-roster-snapshot-import</c>):
/// <see cref="Resolve"/> mirrors <c>resolveSnapshotDiff</c> applied along the chain, and <see cref="Fix"/> mirrors
/// <c>fixSnapshot</c>'s floor values. Equipment order is left as stored: the importer places each item by its type.
/// </summary>
public static class V1RosterSnapshotChain
{
    /// <summary>V1 <c>Rarity</c> → the lowest V1 <c>RarityStars</c> of that rarity (<c>getMinimumStarsForRarity</c>).</summary>
    private static readonly int[] MinimumStars = [0, 2, 4, 6, 8, 11];

    /// <summary>Every snapshot of the chain in order, base first, trashed ones included (later diffs build on
    /// them). Empty without a base.</summary>
    public static IReadOnlyList<V1ResolvedSnapshot> Resolve(V1RosterSnapshotsState state)
    {
        if (state.Base is not { } baseSnapshot)
        {
            return [];
        }

        var current = new V1ResolvedSnapshot(
            baseSnapshot.Name ?? string.Empty,
            baseSnapshot.DateMillisUtc,
            baseSnapshot.DeletedDateMillisUtc is not null,
            (baseSnapshot.Chars ?? []).Select(ResolveCharacter).ToList(),
            [.. baseSnapshot.Mows ?? []]);
        var resolved = new List<V1ResolvedSnapshot> { current };
        foreach (var diff in state.Diffs ?? [])
        {
            current = Apply(current, diff);
            resolved.Add(current);
        }

        return resolved;
    }

    /// <summary>V1 <c>resolveSnapshotDiff</c>: a unit the diff names is updated field by field (an absent field keeps
    /// the previous value), and a unit new to the chain is added with absent fields as 0.</summary>
    public static V1ResolvedSnapshot Apply(V1ResolvedSnapshot previous, V1RosterSnapshotDiff diff)
    {
        var characters = previous.Characters.ToList();
        foreach (var unit in diff.CharDiffs ?? [])
        {
            var index = characters.FindIndex(character => character.Id == unit.Id);
            if (index < 0)
            {
                characters.Add(new V1ResolvedCharacter(
                    unit.Id,
                    unit.Rank ?? 0,
                    unit.Rarity ?? 0,
                    unit.Stars ?? 0,
                    unit.Shards ?? 0,
                    unit.MythicShards ?? 0,
                    unit.Active ?? 0,
                    unit.Passive ?? 0,
                    unit.XpLevel ?? 0,
                    [
                        (EquipmentId(unit.Equip0), unit.Equip0Level),
                        (EquipmentId(unit.Equip1), unit.Equip1Level),
                        (EquipmentId(unit.Equip2), unit.Equip2Level),
                    ]));
                continue;
            }

            var before = characters[index];
            characters[index] = before with
            {
                Rank = unit.Rank ?? before.Rank,
                Rarity = unit.Rarity ?? before.Rarity,
                Stars = unit.Stars ?? before.Stars,
                Shards = unit.Shards ?? before.Shards,
                MythicShards = unit.MythicShards ?? before.MythicShards,
                ActiveAbilityLevel = unit.Active ?? before.ActiveAbilityLevel,
                PassiveAbilityLevel = unit.Passive ?? before.PassiveAbilityLevel,
                XpLevel = unit.XpLevel ?? before.XpLevel,
                Equipment =
                [
                    Slot(before, 0, unit.Equip0, unit.Equip0Level),
                    Slot(before, 1, unit.Equip1, unit.Equip1Level),
                    Slot(before, 2, unit.Equip2, unit.Equip2Level),
                ],
            };
        }

        var mows = previous.Mows.ToList();
        foreach (var unit in diff.MowDiffs ?? [])
        {
            var index = mows.FindIndex(mow => mow.Id == unit.Id);
            if (index < 0)
            {
                mows.Add(new V1SnapshotMow(
                    unit.Id,
                    unit.Rarity ?? 0,
                    unit.Stars ?? 0,
                    unit.Active ?? 0,
                    unit.Passive ?? 0,
                    unit.Shards ?? 0,
                    unit.MythicShards ?? 0,
                    unit.Locked ?? false));
                continue;
            }

            var before = mows[index];
            mows[index] = before with
            {
                Rarity = unit.Rarity ?? before.Rarity,
                Stars = unit.Stars ?? before.Stars,
                PrimaryAbilityLevel = unit.Active ?? before.PrimaryAbilityLevel,
                SecondaryAbilityLevel = unit.Passive ?? before.SecondaryAbilityLevel,
                Shards = unit.Shards ?? before.Shards,
                MythicShards = unit.MythicShards ?? before.MythicShards,
                Locked = unit.Locked ?? before.Locked,
            };
        }

        return new V1ResolvedSnapshot(
            diff.Name ?? string.Empty, diff.DateMillisUtc, diff.DeletedDateMillisUtc is not null, characters, mows);
    }

    /// <summary>V1 <c>fixSnapshot</c>: ability and XP levels of at least 1, rarity at least the unit's initial rarity
    /// (<paramref name="initialRarity"/>, a V1 <c>Rarity</c>, null when unknown) and stars at least that rarity's
    /// minimum.</summary>
    public static V1ResolvedCharacter Fix(V1ResolvedCharacter unit, int? initialRarity)
    {
        var rarity = Math.Max(unit.Rarity, initialRarity ?? 0);
        return unit with
        {
            ActiveAbilityLevel = Math.Max(1, unit.ActiveAbilityLevel),
            PassiveAbilityLevel = Math.Max(1, unit.PassiveAbilityLevel),
            XpLevel = Math.Max(1, unit.XpLevel),
            Rarity = rarity,
            Stars = Math.Max(unit.Stars, MinimumStarsFor(rarity)),
        };
    }

    /// <inheritdoc cref="Fix(V1ResolvedCharacter, int?)"/>
    public static V1SnapshotMow Fix(V1SnapshotMow unit, int? initialRarity)
    {
        var rarity = Math.Max(unit.Rarity, initialRarity ?? 0);
        return unit with
        {
            PrimaryAbilityLevel = Math.Max(1, unit.PrimaryAbilityLevel),
            SecondaryAbilityLevel = Math.Max(1, unit.SecondaryAbilityLevel),
            Rarity = rarity,
            Stars = Math.Max(unit.Stars, MinimumStarsFor(rarity)),
        };
    }

    public static int MinimumStarsFor(int rarity) =>
        rarity >= 0 && rarity < MinimumStars.Length ? MinimumStars[rarity] : 0;

    /// <summary>An equipment id from V1's whole equipment object (a base snapshot) or a bare id (a diff).</summary>
    public static string? EquipmentId(JsonElement? equipment) => equipment switch
    {
        { ValueKind: JsonValueKind.String } value => NullIfBlank(value.GetString()),
        { ValueKind: JsonValueKind.Object } value when value.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String => NullIfBlank(id.GetString()),
        _ => null,
    };

    private static V1ResolvedCharacter ResolveCharacter(V1SnapshotCharacter unit) => new(
        unit.Id,
        unit.Rank,
        unit.Rarity,
        unit.Stars,
        unit.Shards,
        unit.MythicShards,
        unit.ActiveAbilityLevel,
        unit.PassiveAbilityLevel,
        unit.XpLevel,
        [
            (EquipmentId(unit.Equip0), unit.Equip0Level),
            (EquipmentId(unit.Equip1), unit.Equip1Level),
            (EquipmentId(unit.Equip2), unit.Equip2Level),
        ]);

    // V1 replaces an item only when the diff names one, and a level whenever the diff carries it.
    private static (string? EquipmentId, int? Level) Slot(
        V1ResolvedCharacter before, int index, JsonElement? equipment, int? level)
    {
        var previous = before.Equipment.ElementAtOrDefault(index);
        return (EquipmentId(equipment) ?? previous.EquipmentId, level ?? previous.Level);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
