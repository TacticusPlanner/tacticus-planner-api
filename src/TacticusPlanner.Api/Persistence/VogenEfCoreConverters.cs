using TacticusPlanner.Api.Persistence.Users;
using Vogen;

namespace TacticusPlanner.Api.Persistence;

[EfCoreConverter<AccountId>]
[EfCoreConverter<ProfileId>]
internal sealed partial class VogenEfCoreConverters;
