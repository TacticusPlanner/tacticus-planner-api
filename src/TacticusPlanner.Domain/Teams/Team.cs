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
    /// <summary>Optimistic-concurrency counter, bumped on every saved edit. An update must send the revision it read,
    /// or it is refused as stale. Reorders and closed gaps do not change it.</summary>
    public long Revision { get; set; }

    /// <summary>The profile that owns the team; every read is scoped to it by the query filter.</summary>
    public ProfileId ProfileId { get; set; }

    /// <summary>The display name, trimmed; 3 to 40 characters and unique within the profile, ignoring case.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The trimmed, invariant-lowercased name; unique within the profile.</summary>
    public string NameKey { get; private set; } = string.Empty;

    /// <summary>For a team imported from V1, its original V1 name trimmed and lowercased, before any shortening
    /// (<c>v1-team-import</c>). Set once at creation, never changed by a rename, and null for teams made in V2.</summary>
    public string? ImportKey { get; private set; }

    /// <summary>The team's 1-based position in the profile's list, served as <c>position</c>. Contiguous across the
    /// profile's teams; written by reorders and when a delete closes a gap.</summary>
    public int SortOrder { get; set; }

    /// <summary>The game modes the team is built for, distinct and in enum order. Each mode adds its own rules
    /// (<see cref="TeamRules"/>).</summary>
    public List<TeamMode> Modes { get; private set; } = [];

    /// <summary>The characters in lineup order, as unit ids: the core first, then the flex section from
    /// <see cref="FlexStartIndex"/>.</summary>
    public List<string> CharacterIds { get; private set; } = [];

    /// <summary>The index in <see cref="CharacterIds"/> of the first flex character, or null when every character is
    /// core. Flex characters are substitutes that do not count towards the core limits.</summary>
    public int? FlexStartIndex { get; private set; }

    /// <summary>The Machines of War in lineup order, as unit ids: the core first, then the flex section from
    /// <see cref="MowFlexStartIndex"/>.</summary>
    public List<string> MowIds { get; private set; } = [];

    /// <summary>The index in <see cref="MowIds"/> of the first flex MoW, or null when every MoW is core; 0 means
    /// every MoW is flex (design D12).</summary>
    public int? MowFlexStartIndex { get; private set; }

    /// <summary>The campaign group a Campaign team is built for: its characters must come from that campaign's allied
    /// factions and its core must hold the campaign's required characters. Required with the Campaign mode and null
    /// without it.</summary>
    public string? CampaignGroupId { get; private set; }

    /// <summary>The Incursion MoWs an Incursion team is built for, as unit ids: its characters must come from a faction
    /// every one of them allows. Required with the Incursion mode and empty without it.</summary>
    public List<string> IncursionMowIds { get; private set; } = [];

    /// <summary>Free-text notes for the player, trimmed; null when blank. At most 300 characters.</summary>
    public string? Notes { get; private set; }

    /// <summary>The rarity the team is prepared for, so clients can preview the lineup capped at it; null for no cap.
    /// No rule reads it (design D13).</summary>
    public TeamRarityCap? RarityCap { get; private set; }

    /// <summary>Navigation to the owning profile.</summary>
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
