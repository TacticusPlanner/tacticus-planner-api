using TacticusPlanner.Persistence.Users;
using TacticusPlanner.Persistence.Users.Guilds;
using Vogen;

namespace TacticusPlanner.Persistence;

[EfCoreConverter<AccountId>]
[EfCoreConverter<ProfileId>]
[EfCoreConverter<GuildId>]
[EfCoreConverter<GuildMemberId>]
internal sealed partial class VogenEfCoreConverters;
