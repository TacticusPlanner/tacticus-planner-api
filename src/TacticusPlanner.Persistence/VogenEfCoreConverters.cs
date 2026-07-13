using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using Vogen;

namespace TacticusPlanner.Persistence;

[EfCoreConverter<AccountId>]
[EfCoreConverter<ProfileId>]
[EfCoreConverter<GuildId>]
[EfCoreConverter<GuildMemberId>]
[EfCoreConverter<TacticusUserId>]
[EfCoreConverter<TacticusGuildId>]
[EfCoreConverter<CampaignId>]
[EfCoreConverter<UnitId>]
internal sealed partial class VogenEfCoreConverters;
