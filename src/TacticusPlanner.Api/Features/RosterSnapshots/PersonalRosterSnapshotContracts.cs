using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.RosterSnapshots;

namespace TacticusPlanner.Api.Features.RosterSnapshots;

public sealed class CreatePersonalRosterSnapshotRequest
{
    /// <summary>Optional, trimmed, at most 60 characters. Absent or blank names the snapshot after its UTC capture
    /// date (<c>YYYY-MM-DD</c>).</summary>
    public string? Name { get; init; }

    /// <summary>True copies the last synced roster without calling the Tacticus API (and is not rate limited);
    /// false (the default) syncs first.</summary>
    [QueryParam]
    public bool? FromLastSync { get; init; }
}

public sealed class CreatePersonalRosterSnapshotValidator : Validator<CreatePersonalRosterSnapshotRequest>
{
    public CreatePersonalRosterSnapshotValidator()
    {
        RuleFor(request => request.Name)
            .Must(name => name!.Trim().Length <= PersonalRosterSnapshotValidation.MaxNameLength)
            .WithMessage($"name must be at most {PersonalRosterSnapshotValidation.MaxNameLength} characters.")
            .When(request => request.Name is not null);
    }
}

public sealed record RenamePersonalRosterSnapshotRequest(string? Name);

public sealed class RenamePersonalRosterSnapshotValidator : Validator<RenamePersonalRosterSnapshotRequest>
{
    public RenamePersonalRosterSnapshotValidator()
    {
        RuleFor(request => request.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("name must not be blank.");
        RuleFor(request => request.Name)
            .Must(name => name!.Trim().Length <= PersonalRosterSnapshotValidation.MaxNameLength)
            .WithMessage($"name must be at most {PersonalRosterSnapshotValidation.MaxNameLength} characters.")
            .When(request => request.Name is not null);
    }
}

/// <summary>A personal snapshot without its roster.</summary>
/// <param name="TakenAt">When the roster was read (UTC).</param>
public sealed record PersonalRosterSnapshotResponse(
    Guid Id,
    string Name,
    RosterSnapshotSource Source,
    DateTimeOffset TakenAt,
    int CharacterCount,
    int MowCount
)
{
    public static PersonalRosterSnapshotResponse From(PersonalRosterSnapshot snapshot) => new(
        snapshot.Id.Value, snapshot.Name, snapshot.Source, snapshot.TakenAt, snapshot.CharacterCount, snapshot.MowCount);
}

/// <param name="Limit">How many snapshots that count toward the limit the profile may hold.</param>
/// <param name="Count">How many it holds (manual and V1 imports; automatic snapshots do not count).</param>
/// <param name="Items">Newest <c>takenAt</c> first.</param>
/// <param name="Limit">The limit on snapshots that count toward it (manual and V1-imported).</param>
/// <param name="Count">The caller's snapshots that count toward <paramref name="Limit"/>.</param>
/// <param name="AutoLimit">The rolling quota of automatic snapshots.</param>
/// <param name="AutoCount">The caller's automatic snapshots.</param>
public sealed record PersonalRosterSnapshotListResponse(
    int Limit,
    int Count,
    int AutoLimit,
    int AutoCount,
    IReadOnlyList<PersonalRosterSnapshotResponse> Items
);
