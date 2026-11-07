using System.Text.Json.Serialization;
using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;
using Vogen;

namespace TacticusPlanner.Domain.RosterSnapshots;

[ValueObject<Guid>]
public readonly partial struct PersonalRosterSnapshotId : IGuidValueObject;

/// <summary>Who took a personal snapshot. Stored as its C# name, served camelCase.</summary>
public enum RosterSnapshotSource
{
    [JsonStringEnumMemberName("manual")] Manual,
    [JsonStringEnumMemberName("auto")] Auto,
    [JsonStringEnumMemberName("v1Import")] V1Import,
}

/// <summary>
/// A named copy of the profile's whole roster that the profile keeps (<c>personal-roster-snapshots</c>). The
/// roster is a full copy taken by <c>RosterSnapshotFactory</c>, never a diff, and never changes after creation;
/// only <see cref="Name"/> does.
/// </summary>
public class PersonalRosterSnapshot : BaseEntity<PersonalRosterSnapshotId>
{
    public ProfileId ProfileId { get; set; }

    /// <summary>Trimmed, 1–60 characters, not unique.</summary>
    public string Name { get; set; } = string.Empty;

    public RosterSnapshotSource Source { get; set; }

    /// <summary>When the roster was read (UTC); equal to <c>Roster.TakenAt</c>. A column of its own so the list
    /// can order by it without reading the roster.</summary>
    public DateTimeOffset TakenAt { get; set; }

    public int CharacterCount { get; set; }

    public int MowCount { get; set; }

    public RosterSnapshot Roster { get; set; } = new();

    public virtual Profile? Profile { get; set; }

    /// <summary>A new snapshot of <paramref name="roster"/>, named <paramref name="name"/> or, when that is blank,
    /// the ISO date of the roster's <c>TakenAt</c>.</summary>
    public static PersonalRosterSnapshot Create(
        ProfileId profileId, string? name, RosterSnapshotSource source, RosterSnapshot roster)
    {
        var snapshot = new PersonalRosterSnapshot
        {
            Id = PersonalRosterSnapshotId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Name = PersonalRosterSnapshotValidation.NameOrDefault(name, roster.TakenAt),
            Source = source,
            TakenAt = roster.TakenAt,
            CharacterCount = roster.Characters.Count,
            MowCount = roster.Mows.Count,
            Roster = roster,
        };
        snapshot.EnsureValid();
        return snapshot;
    }

    /// <summary>Renames the snapshot; the roster and <see cref="TakenAt"/> are left as they are.</summary>
    public void Rename(string name)
    {
        Name = name.Trim();
        EnsureValid();
    }

    /// <summary>Keeps an automatic snapshot: it becomes <see cref="RosterSnapshotSource.Manual"/>, with its name,
    /// <see cref="TakenAt"/> and roster unchanged, so the automatic quota can no longer replace it.</summary>
    public void Keep()
    {
        if (Source != RosterSnapshotSource.Auto)
            throw new InvalidOperationException("Only an automatic roster snapshot can be kept.");
        Source = RosterSnapshotSource.Manual;
    }

    /// <summary>The invariants the database does not enforce, checked before a save.</summary>
    public void EnsureValid()
    {
        if (!PersonalRosterSnapshotValidation.IsValidName(Name))
            throw new InvalidOperationException("A roster snapshot name must be 1 to 60 characters after trimming.");
        if (TakenAt != Roster.TakenAt || CharacterCount != Roster.Characters.Count || MowCount != Roster.Mows.Count)
            throw new InvalidOperationException("A roster snapshot's metadata must match its roster.");
    }
}

public static class PersonalRosterSnapshotValidation
{
    public const int MaxNameLength = 60;

    /// <summary>The default per-profile limit on snapshots that count toward it.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The default rolling quota of automatic snapshots per profile.</summary>
    public const int DefaultAutoLimit = 12;

    /// <summary>The default number of days without any roster snapshot after which a sync takes an automatic one.</summary>
    public const int DefaultAutoWindowDays = 30;

    public static bool IsValidName(string? name) =>
        name is not null && name == name.Trim() && name.Length is >= 1 and <= MaxNameLength;

    /// <summary>The trimmed name, or the ISO UTC date of <paramref name="takenAt"/> when it is absent or blank.</summary>
    public static string NameOrDefault(string? name, DateTimeOffset takenAt) =>
        string.IsNullOrWhiteSpace(name)
            ? takenAt.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : name.Trim();

    /// <summary>Whether a snapshot of <paramref name="source"/> counts toward the per-profile limit.</summary>
    public static bool CountsTowardLimit(RosterSnapshotSource source) => source != RosterSnapshotSource.Auto;
}
