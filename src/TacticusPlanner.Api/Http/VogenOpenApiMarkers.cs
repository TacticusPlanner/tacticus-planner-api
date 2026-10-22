using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using Vogen;

[assembly: VogenDefaults(openApiSchemaCustomizations: OpenApiSchemaCustomizations.GenerateOpenApiMappingExtensionMethod)]

namespace TacticusPlanner.Api.Http;

/// <summary>
/// The value objects Vogen describes in the OpenAPI document as the primitive they are written as
/// (<c>MapVogenTypesInVogenOpenApiMarkers</c>, generated). The pipeline alone emits an empty schema for them.
/// A new value object used in a contract must be listed here; <c>EverySchemaDescribesItsShape</c> fails otherwise.
/// </summary>
[OpenApiMarker<CampaignBattleId>]
[OpenApiMarker<CampaignId>]
[OpenApiMarker<GoalId>]
[OpenApiMarker<GuildId>]
[OpenApiMarker<GuildMemberId>]
[OpenApiMarker<GuildRaidAttackId>]
[OpenApiMarker<GuildRaidSeasonId>]
[OpenApiMarker<LegendaryEventPlanId>]
[OpenApiMarker<LegendaryEventRunRecordId>]
[OpenApiMarker<LegendaryEventTeamId>]
[OpenApiMarker<ProfileId>]
[OpenApiMarker<ProjectId>]
[OpenApiMarker<TacticusGuildId>]
[OpenApiMarker<TacticusUserId>]
[OpenApiMarker<UnitId>]
public partial class VogenOpenApiMarkers;
