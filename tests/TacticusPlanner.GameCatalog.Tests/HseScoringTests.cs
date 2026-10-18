using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class HseScoringTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static GameCatalogEventDefinition Definition(string id, string type = "HomeScreenEvent", JsonElement? config = null) =>
        new(id, type, new GameCatalogEventRecurrence("None", null, null, null), [], config);

    private static GameCatalogEventOccurrence Occurrence(
        string id, string definitionId, IReadOnlyDictionary<string, JsonElement>? parameters = null) =>
        new(id, definitionId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch + TimeSpan.FromDays(3), parameters);

    private static GameCatalogHseVariant Variant(
        string key,
        string scope = "Personal",
        IReadOnlyList<GameCatalogHseTier>? tiers = null,
        IReadOnlyList<GameCatalogHseTracker>? trackers = null) =>
        new(
            key,
            scope,
            tiers ?? [new GameCatalogHseTier("High", [new GameCatalogHseMilestone(10, "gold", 1), new GameCatalogHseMilestone(20, "gold", 2)], null)],
            trackers ?? [new GameCatalogHseTracker("KillUnits", 1, null, null, null, null)],
            []);

    private static List<GameCatalogValidationError> Validate(
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogEventOccurrence> occurrences,
        IReadOnlyList<GameCatalogHseScoring> scoring)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateHseScoring(definitions, occurrences, scoring, errors);
        return errors;
    }

    private static JsonElement ServedHse(string definitionId) =>
        GameCatalogLoader.Load().EventDefinitionViews.Single(definition => definition.Id == definitionId).Config!.Value.GetProperty("hse");

    private static JsonElement ServedVariant(string definitionId, string variantKey) =>
        ServedHse(definitionId).GetProperty("variants").EnumerateArray()
            .Single(variant => variant.GetProperty("variantKey").GetString() == variantKey);

    [Fact]
    public void MergesScoringIntoConfigKeepingExistingKeys()
    {
        var withConfig = Definition("hse-a", config: Json(new { keep = 7 }));
        var withoutScoring = Definition("hse-b");

        var views = GameCatalogDenormalizer.BuildEventDefinitions(
            [withConfig, withoutScoring], [new GameCatalogHseScoring("hse-a", [Variant("default")])]);

        var config = views[0].Config!.Value;
        Assert.Equal(7, config.GetProperty("keep").GetInt32());
        Assert.Equal("default", config.GetProperty("hse").GetProperty("variants")[0].GetProperty("variantKey").GetString());
        Assert.Null(views[1].Config);
    }

    [Fact]
    public void ServesPsykerBoostScoring()
    {
        var variant = ServedVariant("hse-trait-boost-psyker", "default");

        Assert.Equal("Personal", variant.GetProperty("scope").GetString());
        Assert.Equal(["High", "Mid", "Low"], variant.GetProperty("tiers").EnumerateArray().Select(tier => tier.GetProperty("tier").GetString()!));

        var high = variant.GetProperty("tiers")[0];
        Assert.Equal(19, high.GetProperty("milestones").GetArrayLength());
        Assert.Equal(765, high.GetProperty("milestones")[18].GetProperty("requiredPoints").GetInt32());
        Assert.Equal(50, high.GetProperty("repeat").GetProperty("points").GetInt32());

        var tracker = variant.GetProperty("trackers").EnumerateArray().Single();
        Assert.Equal("KillUnits", tracker.GetProperty("kind").GetString());
        Assert.Equal(1, tracker.GetProperty("points").GetInt32());
        Assert.Equal(["Arena", "SalvageRun", "Onslaught"], tracker.GetProperty("gameModes").GetProperty("allowed").EnumerateArray().Select(mode => mode.GetString()!));
        Assert.Equal("Psyker", tracker.GetProperty("player").GetProperty("tags").GetProperty("allowed")[0].GetString());

        var modifiers = variant.GetProperty("modifiers").EnumerateArray().ToArray();
        var xp = modifiers.Single(modifier => modifier.GetProperty("kind").GetString() == "UnitXp");
        Assert.Equal(3.0, xp.GetProperty("multiplier").GetDouble());
        Assert.False(xp.TryGetProperty("healthPct", out _));
        var stats = modifiers.Single(modifier => modifier.GetProperty("kind").GetString() == "StatBoost");
        Assert.Equal(20, stats.GetProperty("healthPct").GetInt32());
        Assert.False(stats.TryGetProperty("multiplier", out _));
    }

    [Fact]
    public void ServesOneFactionBoostVariantPerFactionAndGlobalVariantsWithGlobalScope()
    {
        var factionKeys = ServedHse("hse-faction-boost").GetProperty("variants").EnumerateArray()
            .Select(variant => variant.GetProperty("variantKey").GetString()!).ToArray();
        Assert.Equal(["AdeptusMechanicus", "DarkAngels", "SpaceWolves"], factionKeys);

        Assert.Equal("Global", ServedVariant("hse-11th-edition-week-1", "global-orks").GetProperty("scope").GetString());
        Assert.Equal("Global", ServedVariant("hse-global-operation-imperator", "default").GetProperty("scope").GetString());
    }

    [Fact]
    public void DropsCampaignSweepTrackersAndLeavesUnscoredDefinitionsAlone()
    {
        // V1's Machine Hunt also has two raidBattles (campaign sweep) trackers; serving them would double-count.
        var machineHunt = ServedVariant("hse-machine-hunt", "default");
        var trackers = machineHunt.GetProperty("trackers").EnumerateArray()
            .Select(tracker => (Kind: tracker.GetProperty("kind").GetString(), Points: tracker.GetProperty("points").GetInt32()))
            .ToArray();
        Assert.Equal([("KillUnits", 3), ("KillUnits", 5)], trackers);

        var againstTheTide = GameCatalogLoader.Load().EventDefinitionViews.Single(definition => definition.Id == "hse-against-the-tide");
        Assert.False(againstTheTide.Config is { ValueKind: JsonValueKind.Object } config && config.TryGetProperty("hse", out _));
    }

    [Fact]
    public void BundledScoringPassesValidation()
    {
        var snapshot = GameCatalogLoader.Load();
        var errors = Validate(snapshot.EventDefinitions, snapshot.EventOccurrences, snapshot.HseScoring);
        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownOrNonHseDefinitionFails()
    {
        var errors = Validate(
            [Definition("le-x", type: "LegendaryEvent")],
            [],
            [new GameCatalogHseScoring("missing", [Variant("default")]), new GameCatalogHseScoring("le-x", [Variant("default")])]);

        Assert.Contains(errors, error => error.Code == "MissingReference");
        Assert.Contains(errors, error => error.Code == "InvalidReference");
    }

    [Fact]
    public void InvalidVariantContentFails()
    {
        var badTiers = new[]
        {
            new GameCatalogHseTier("Top", [new GameCatalogHseMilestone(20, "gold", 1), new GameCatalogHseMilestone(20, "gold", 0)], new GameCatalogHseRepeat(0, "gold", 1, null)),
        };
        var badTrackers = new[] { new GameCatalogHseTracker("Dance", 0, null, null, null, null) };

        var errors = Validate(
            [Definition("hse-a")],
            [],
            [new GameCatalogHseScoring("hse-a", [Variant("default", scope: "Guild", tiers: badTiers, trackers: badTrackers), Variant("default")])]);

        Assert.Contains(errors, error => error.Message.Contains("scope 'Guild'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("not a recognized tier", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Code == "InvalidLadder" && error.Message.Contains("strictly increasing", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("quantity 0", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("repeat step", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("tracker kind 'Dance'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("0 points", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Code == "DuplicateId");
    }

    [Fact]
    public void HseVariantMustNameAVariant()
    {
        var errors = Validate(
            [Definition("hse-a"), Definition("hse-b")],
            [
                Occurrence("occ-ok", "hse-a", new Dictionary<string, JsonElement> { ["hseVariant"] = Json("october") }),
                Occurrence("occ-unknown", "hse-a", new Dictionary<string, JsonElement> { ["hseVariant"] = Json("november") }),
                Occurrence("occ-unscored", "hse-b", new Dictionary<string, JsonElement> { ["hseVariant"] = Json("october") }),
            ],
            [new GameCatalogHseScoring("hse-a", [Variant("default"), Variant("october")])]);

        var invalid = errors.Where(error => error.Code == "InvalidHseVariant").Select(error => error.Message).ToArray();
        Assert.Equal(2, invalid.Length);
        Assert.Contains(invalid, message => message.Contains("occ-unknown", StringComparison.Ordinal));
        Assert.Contains(invalid, message => message.Contains("occ-unscored", StringComparison.Ordinal));
    }

    [Fact]
    public void OccurrenceMustResolveByTargetFactionOrDefault()
    {
        var errors = Validate(
            [Definition("hse-boost"), Definition("hse-plain")],
            [
                Occurrence("occ-faction", "hse-boost", new Dictionary<string, JsonElement> { ["targetFactionId"] = Json("DarkAngels") }),
                Occurrence("occ-orphan", "hse-boost", new Dictionary<string, JsonElement> { ["targetFactionId"] = Json("Orks") }),
                Occurrence("occ-none", "hse-boost"),
                Occurrence("occ-unscored", "hse-plain"),
            ],
            [new GameCatalogHseScoring("hse-boost", [Variant("DarkAngels")])]);

        var unresolved = errors.Where(error => error.Code == "UnresolvedHseVariant").Select(error => error.Message).ToArray();
        Assert.Equal(2, unresolved.Length);
        Assert.Contains(unresolved, message => message.Contains("occ-orphan", StringComparison.Ordinal));
        Assert.Contains(unresolved, message => message.Contains("occ-none", StringComparison.Ordinal));
    }

    [Fact]
    public void ScoredDefinitionConfigMustBeAnObjectWithoutHse()
    {
        var errors = Validate(
            [Definition("hse-array", config: Json("not-an-object")), Definition("hse-authored", config: Json(new { hse = 1 })), Definition("hse-ok", config: Json(new { keep = 1 }))],
            [],
            [
                new GameCatalogHseScoring("hse-array", [Variant("default")]),
                new GameCatalogHseScoring("hse-authored", [Variant("default")]),
                new GameCatalogHseScoring("hse-ok", [Variant("default")]),
            ]);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Message.Contains("'hse-array'", StringComparison.Ordinal) && error.Message.Contains("not an object", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("'hse-authored'", StringComparison.Ordinal) && error.Message.Contains("config.hse", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateTiersUnknownGameModesAndBadModifiersFail()
    {
        var tier = new GameCatalogHseTier("High", [new GameCatalogHseMilestone(10, "gold", 1)], null);
        var trackers = new[]
        {
            new GameCatalogHseTracker("KillUnits", 1, null, new GameCatalogHseRestriction(["Pvp"], null), null, null),
        };
        var variant = Variant("default", tiers: [tier, tier], trackers: trackers) with
        {
            Modifiers =
            [
                new GameCatalogHseModifier("Dance", null, null, null, null, null, null, null, null),
                new GameCatalogHseModifier("UnitXp", null, null, false, null, null, null, null, null),
                new GameCatalogHseModifier("UnitXp", 2, new Dictionary<string, double> { ["Common"] = 2 }, false, null, null, null, null, null),
                new GameCatalogHseModifier("UnitXp", 3, null, false, null, null, null, new GameCatalogHseRestriction(null, ["Raids"]), null),
            ],
        };

        var errors = Validate([Definition("hse-a")], [], [new GameCatalogHseScoring("hse-a", [variant])]);

        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("hse-a/default/High", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("unknown game mode 'Pvp'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("unknown game mode 'Raids'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("modifier kind 'Dance'", StringComparison.Ordinal));
        Assert.Equal(2, errors.Count(error => error.Message.Contains("exactly one of multiplier", StringComparison.Ordinal)));
    }
}
