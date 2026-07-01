using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class LegendaryEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("lanes")]
    public List<LegendaryEventLane> Lanes { get; set; }

    [JsonPropertyName("currentPoints")]
    public int CurrentPoints { get; set; }

    [JsonPropertyName("currentCurrency")]
    public int CurrentCurrency { get; set; }

    [JsonPropertyName("currentShards")]
    public int CurrentShards { get; set; }

    [JsonPropertyName("currentClaimedChestIndex")]
    public int CurrentClaimedChestIndex { get; set; }

    [JsonPropertyName("currentEvent")]
    public LegendaryEventCurrentEvent CurrentEvent { get; set; }
}

public class LegendaryEventLane
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("battleConfigs")]
    public List<LegendaryEventBattleConfig> BattleConfigs { get; set; }

    [JsonPropertyName("progress")]
    public List<LegendaryEventProgress> Progress { get; set; }
}

public class LegendaryEventBattleConfig
{
    [JsonPropertyName("numEnemies")]
    public int NumEnemies { get; set; }

    [JsonPropertyName("objectives")]
    public List<LegendaryEventObjective> Objectives { get; set; }

    [JsonPropertyName("disallowedFactions")]
    public List<string> DisallowedFactions { get; set; }
}
public class LegendaryEventObjective
{
    [JsonPropertyName("objectiveType")]
    public string ObjectiveType { get; set; }

    [JsonPropertyName("objectiveTarget")]
    public string ObjectiveTarget { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }
}

public class LegendaryEventProgress
{
    [JsonPropertyName("objectivesCleared")]
    public List<int> ObjectivesCleared { get; set; }

    [JsonPropertyName("highScore")]
    public int HighScore { get; set; }

    [JsonPropertyName("encounterPoints")]
    public int EncounterPoints { get; set; }
}

public class LegendaryEventCurrentEvent
{
    [JsonPropertyName("run")]
    public int Run { get; set; }

    [JsonPropertyName("tokens")]
    public LegendaryEventTokens Tokens { get; set; }

    [JsonPropertyName("hasUsedAdForExtraTokenToday")]
    public bool HasUsedAdForExtraTokenToday { get; set; }

    [JsonPropertyName("extraCurrencyPerPayout")]
    public int ExtraCurrencyPerPayout { get; set; }
}

public class LegendaryEventTokens
{
    [JsonPropertyName("current")]
    public int CurrentTokens { get; set; }

    [JsonPropertyName("max")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("nextTokenInSeconds")]
    public int NextTokenInSeconds { get; set; }

    [JsonPropertyName("regenDelayInSeconds")]
    public int RegenDelayInSeconds { get; set; }
}
