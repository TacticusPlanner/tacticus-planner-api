using TacticusPlanner.Persistence.Users;
using Vogen;

namespace TacticusPlanner.Persistence;

[EfCoreConverter<AccountId>]
[EfCoreConverter<ProfileId>]
internal sealed partial class VogenEfCoreConverters;
