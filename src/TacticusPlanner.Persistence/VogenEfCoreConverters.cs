using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using Vogen;

namespace TacticusPlanner.Persistence;

// GoalId/ProjectId were simply never added here when those value objects were introduced — that (not a
// genuine Vogen limitation) is why GoalConfiguration/ProjectConfiguration/ProjectGoalConfiguration/
// ProfileConfiguration previously carried hand-written .HasConversion(id => id.Value, value => X.From(value))
// blocks instead of .HasVogenConversion() for them (see https://stevedunn.github.io/Vogen/efcoreintegrationhowto.html
// for how this marker class feeds Vogen's source generator + RegisterAllInVogenEfCoreConverters()).
[EfCoreConverter<AccountId>]
[EfCoreConverter<ProfileId>]
[EfCoreConverter<GuildId>]
[EfCoreConverter<GuildMemberId>]
[EfCoreConverter<TacticusUserId>]
[EfCoreConverter<TacticusGuildId>]
[EfCoreConverter<CampaignId>]
[EfCoreConverter<UnitId>]
[EfCoreConverter<GoalId>]
[EfCoreConverter<ProjectId>]
internal sealed partial class VogenEfCoreConverters;
