namespace TacticusPlanner.Api.Features.UserJot;

public sealed class UserJotOptions
{
    public const string SectionName = "UserJot";

    public string ProjectId { get; set; } = string.Empty;

    public string ProjectSecret { get; set; } = string.Empty;
}
