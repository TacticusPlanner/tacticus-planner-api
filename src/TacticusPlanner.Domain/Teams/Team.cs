using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;
using Vogen;

namespace TacticusPlanner.Domain.Teams;

[ValueObject<Guid>]
public readonly partial struct TeamId : IGuidValueObject;

/// <summary>
/// The editable content of a team: everything a create or update replaces and the rules read. Lists are in lineup
/// order; <see cref="FlexStartIndex"/> is the index of the first flex character in <see cref="CharacterIds"/>, null
/// when the team has none, and <see cref="MowFlexStartIndex"/> the same for <see cref="MowIds"/> (design D12), where
/// 0 means every MoW is flex. <see cref="RarityCap"/> is what the team is prepared for; no rule reads it (D13).
/// </summary>
public sealed record TeamDraft(
    string Name,
    IReadOnlyList<TeamMode> Modes,
    IReadOnlyList<string> CharacterIds,
    int? FlexStartIndex,
    IReadOnlyList<string> MowIds,
    string? CampaignGroupId,
    IReadOnlyList<string> IncursionMowIds,
    string? Notes,
    int? MowFlexStartIndex = null,
    TeamRarityCap? RarityCap = null
)
{
    /// <summary>The number of core characters: those before the flex section.</summary>
    public int CoreCount => FlexStartIndex ?? CharacterIds.Count;

    /// <summary>The number of core MoWs: those before the MoW flex section.</summary>
    public int CoreMowCount => MowFlexStartIndex ?? MowIds.Count;
}

/// <summary>
/// A reusable lineup owned by one profile (<c>personal-teams</c>). Read and written whole as one row (ADR 0011); its
/// rules are checked by <see cref="TeamRules"/>, never by the database. <see cref="SortOrder"/> is 1-based and
/// contiguous across the profile's teams and is served as <c>position</c>.
/// </summary>
public class Team : BaseEntity<TeamId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The trimmed, invariant-lowercased name; unique within the profile.</summary>
    public string NameKey { get; private set; } = string.Empty;

    /// <summary>For a team imported from V1, its original V1 name trimmed and lowercased, before any shortening
    /// (<c>v1-team-import</c>). Set once at creation, never changed by a rename, and null for teams made in V2.</summary>
    public string? ImportKey { get; private set; }

    public int SortOrder { get; set; }

    public List<TeamMode> Modes { get; private set; } = [];

    public List<string> CharacterIds { get; private set; } = [];

    public int? FlexStartIndex { get; private set; }

    public List<string> MowIds { get; private set; } = [];

    public int? MowFlexStartIndex { get; private set; }

    public string? CampaignGroupId { get; private set; }

    public List<string> IncursionMowIds { get; private set; } = [];

    public string? Notes { get; private set; }

    public TeamRarityCap? RarityCap { get; private set; }

    public virtual Profile? Profile { get; set; }

    public static Team Create(ProfileId profileId, TeamDraft draft, int sortOrder, string? importKey = null)
    {
        var team = new Team
        {
            Id = TeamId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            SortOrder = sortOrder,
            ImportKey = importKey,
        };
        team.Apply(draft);
        return team;
    }

    /// <summary>Replaces every editable field with <paramref name="draft"/>; the id, order and import key stay.</summary>
    public void Apply(TeamDraft draft)
    {
        Name = draft.Name.Trim();
        NameKey = TeamValidation.NameKey(draft.Name);
        Modes = [.. draft.Modes.Distinct().Order()];
        CharacterIds = [.. draft.CharacterIds];
        FlexStartIndex = draft.FlexStartIndex;
        MowIds = [.. draft.MowIds];
        MowFlexStartIndex = draft.MowFlexStartIndex;
        CampaignGroupId = draft.CampaignGroupId;
        IncursionMowIds = [.. draft.IncursionMowIds];
        Notes = TeamValidation.NormalizeNotes(draft.Notes);
        RarityCap = draft.RarityCap;
    }

    public TeamDraft ToDraft() =>
        new(Name, Modes, CharacterIds, FlexStartIndex, MowIds, CampaignGroupId, IncursionMowIds, Notes, MowFlexStartIndex,
            RarityCap);
}
