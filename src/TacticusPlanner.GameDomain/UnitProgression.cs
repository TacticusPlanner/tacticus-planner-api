using System.Text.Json.Serialization;

namespace TacticusPlanner.GameDomain;

/// <summary>Star level: 0 = Common, 3 = Uncommon, 6 = Rare, 9 = Epic, 12 = Legendary — Tacticus's raw
/// per-unit progressionIndex int is a direct 0-based index into this same 20-step (rarity, stars)
/// ladder (confirmed: e.g. index 12 lands exactly on "Legendary:RedThreeStars"), matching the client's
/// progressionOrder exactly. Member names can't contain the client's "Rarity:Stars" separator, so each
/// is given its wire string explicitly via <see cref="JsonStringEnumMemberNameAttribute"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnitProgression
{
    [JsonStringEnumMemberName("Common:None")] CommonNone = 0,
    [JsonStringEnumMemberName("Common:OneStar")] CommonOneStar = 1,
    [JsonStringEnumMemberName("Common:TwoStars")] CommonTwoStars = 2,
    [JsonStringEnumMemberName("Uncommon:TwoStars")] UncommonTwoStars = 3,
    [JsonStringEnumMemberName("Uncommon:ThreeStars")] UncommonThreeStars = 4,
    [JsonStringEnumMemberName("Uncommon:FourStars")] UncommonFourStars = 5,
    [JsonStringEnumMemberName("Rare:FourStars")] RareFourStars = 6,
    [JsonStringEnumMemberName("Rare:FiveStars")] RareFiveStars = 7,
    [JsonStringEnumMemberName("Rare:RedOneStar")] RareRedOneStar = 8,
    [JsonStringEnumMemberName("Epic:RedOneStar")] EpicRedOneStar = 9,
    [JsonStringEnumMemberName("Epic:RedTwoStars")] EpicRedTwoStars = 10,
    [JsonStringEnumMemberName("Epic:RedThreeStars")] EpicRedThreeStars = 11,
    [JsonStringEnumMemberName("Legendary:RedThreeStars")] LegendaryRedThreeStars = 12,
    [JsonStringEnumMemberName("Legendary:RedFourStars")] LegendaryRedFourStars = 13,
    [JsonStringEnumMemberName("Legendary:RedFiveStars")] LegendaryRedFiveStars = 14,
    [JsonStringEnumMemberName("Legendary:OneBlueStar")] LegendaryOneBlueStar = 15,
    [JsonStringEnumMemberName("Mythic:OneBlueStar")] MythicOneBlueStar = 16,
    [JsonStringEnumMemberName("Mythic:TwoBlueStars")] MythicTwoBlueStars = 17,
    [JsonStringEnumMemberName("Mythic:ThreeBlueStars")] MythicThreeBlueStars = 18,
    [JsonStringEnumMemberName("Mythic:MythicWings")] MythicMythicWings = 19,
}
