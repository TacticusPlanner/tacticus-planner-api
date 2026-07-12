using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.PlayerData.Chunks;

/// <summary>0 = Stone I, 3 = Iron I, 6 = Bronze I, 9 = Silver I, 12 = Gold I, 15 = Diamond I, 18 =
/// Adamantine I — Tacticus's raw per-unit rank int is a direct 0-based index into this same 21-step
/// ladder (confirmed against a real player response), matching the client's rankOrder exactly.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnitRank
{
    Stone1 = 0,
    Stone2 = 1,
    Stone3 = 2,
    Iron1 = 3,
    Iron2 = 4,
    Iron3 = 5,
    Bronze1 = 6,
    Bronze2 = 7,
    Bronze3 = 8,
    Silver1 = 9,
    Silver2 = 10,
    Silver3 = 11,
    Gold1 = 12,
    Gold2 = 13,
    Gold3 = 14,
    Diamond1 = 15,
    Diamond2 = 16,
    Diamond3 = 17,
    Adamantine1 = 18,
    Adamantine2 = 19,
    Adamantine3 = 20,
}
