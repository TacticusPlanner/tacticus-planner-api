using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class EventsValidationTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static GameCatalogEventDefinition Definition(string id, params string[] requiredParameters) =>
        new(id, "HomeScreenEvent", new GameCatalogEventRecurrence("None", null, null, null), requiredParameters, null);

    private static GameCatalogEventOccurrence Occurrence(
        string id, string definitionId, IReadOnlyDictionary<string, JsonElement>? parameters = null) =>
        new(id, definitionId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch + TimeSpan.FromDays(3), parameters);

    [Fact]
    public void UnresolvableDefinitionIdFailsValidation()
    {
        var occurrence = Occurrence("occ-1", "does-not-exist");
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([], [occurrence], errors);

        Assert.Contains(errors, error => error.Code == "MissingReference");
    }

    [Fact]
    public void MissingRequiredParameterFailsValidationForFactionBoostButNotFactionFocus()
    {
        var factionBoost = Definition("hse-faction-boost", "targetFactionId");
        var factionFocus = Definition("hse-faction-focus"); // no required parameters
        var boostOccurrenceMissingFaction = Occurrence("occ-boost", "hse-faction-boost");
        var focusOccurrence = Occurrence("occ-focus", "hse-faction-focus");
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents(
            [factionBoost, factionFocus], [boostOccurrenceMissingFaction, focusOccurrence], errors);

        var boostErrors = errors.Where(error => error.Code == "MissingRequiredParameter").ToArray();
        Assert.Single(boostErrors);
        Assert.Contains("occ-boost", boostErrors[0].Message);
        Assert.Contains("targetFactionId", boostErrors[0].Message);
    }

    [Fact]
    public void SupplyingAllRequiredParametersPasses()
    {
        var factionBoost = Definition("hse-faction-boost", "targetFactionId");
        var occurrence = Occurrence("occ-boost", "hse-faction-boost",
            new Dictionary<string, JsonElement> { ["targetFactionId"] = Json("dark-angels") });
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([factionBoost], [occurrence], errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void DefinitionWithNoRequiredParametersNeverFails()
    {
        var factionFocus = Definition("hse-faction-focus");
        var occurrence = Occurrence("occ-focus", "hse-faction-focus");
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([factionFocus], [occurrence], errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void FixedRecurrenceMissingAnchorFailsValidation()
    {
        var definition = new GameCatalogEventDefinition(
            "broken-fixed", "Test", new GameCatalogEventRecurrence("Fixed", 35, 7, null), [], null);
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [], errors);

        Assert.Contains(errors, error => error.Code == "RequiredField");
    }

    [Fact]
    public void FixedRecurrenceWithDurationAtOrAboveIntervalFailsValidation()
    {
        var definition = new GameCatalogEventDefinition(
            "overlapping", "Test", new GameCatalogEventRecurrence("Fixed", 7, 7, DateTimeOffset.UnixEpoch), [], null);
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [], errors);

        Assert.Contains(errors, error => error.Code == "InvalidRecurrence");
    }

    [Fact]
    public void FixedRecurrenceWithNegativeIntervalFailsValidationInsteadOfHanging()
    {
        var definition = new GameCatalogEventDefinition(
            "negative-interval", "Test", new GameCatalogEventRecurrence("Fixed", -1, 1, DateTimeOffset.UnixEpoch), [], null);
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [], errors);

        Assert.Contains(errors, error => error.Code == "InvalidRecurrence");
    }

    [Fact]
    public void FixedRecurrenceWithZeroDurationFailsValidation()
    {
        var definition = new GameCatalogEventDefinition(
            "zero-duration", "Test", new GameCatalogEventRecurrence("Fixed", 7, 0, DateTimeOffset.UnixEpoch), [], null);
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [], errors);

        Assert.Contains(errors, error => error.Code == "InvalidRecurrence");
    }

    [Fact]
    public void UnrecognizedRecurrenceKindFailsValidation()
    {
        var definition = new GameCatalogEventDefinition(
            "typo-kind", "Test", new GameCatalogEventRecurrence("Fixd", null, null, null), [], null);
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [], errors);

        Assert.Contains(errors, error => error.Code == "InvalidRecurrenceKind");
    }

    [Fact]
    public void DuplicateDefinitionIdDoesNotThrowAndOccurrenceValidationStillRuns()
    {
        var first = Definition("dup-id");
        var second = Definition("dup-id");
        var occurrence = Occurrence("occ-1", "does-not-exist");
        var errors = new List<GameCatalogValidationError>();

        // Must not throw — a duplicate definition id used to crash ToDictionary before this occurrence's
        // own MissingReference error could ever be collected.
        GameCatalogValidator.ValidateEvents([first, second], [occurrence], errors);

        Assert.Contains(errors, error => error.Code == "MissingReference");
    }

    [Fact]
    public void OccurrenceWithStartAtOrAfterEndFailsValidation()
    {
        var definition = Definition("hse-faction-focus");
        var invalidOccurrence = new GameCatalogEventOccurrence(
            "occ-inverted", "hse-faction-focus", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [invalidOccurrence], errors);

        Assert.Contains(errors, error => error.Code == "InvalidTimeWindow");
    }

    [Fact]
    public void OccurrenceWithStartAfterEndFailsValidation()
    {
        // Distinct from the equal-boundary case above — this exercises `startUtc > endUtc`, so a
        // regression that swapped `>=` for `>` in the comparison would still be caught.
        var definition = Definition("hse-faction-focus");
        var invalidOccurrence = new GameCatalogEventOccurrence(
            "occ-reversed", "hse-faction-focus",
            DateTimeOffset.UnixEpoch + TimeSpan.FromDays(1), DateTimeOffset.UnixEpoch, null);
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [invalidOccurrence], errors);

        Assert.Contains(errors, error => error.Code == "InvalidTimeWindow");
    }

    private static GameCatalogEventDefinition ShopEventDefinition() =>
        new("anniversary-event", "ShopEvent", new GameCatalogEventRecurrence("None", null, null, null),
            ["weekNumber", "eventKey"], null);

    private static GameCatalogEventOccurrence ShopEventWeek(Dictionary<string, JsonElement> parameters) =>
        Occurrence("occ-shop", "anniversary-event", parameters);

    [Fact]
    public void ShopEventWeekWithoutEventKeyFailsValidation()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents(
            [ShopEventDefinition()], [ShopEventWeek(new() { ["weekNumber"] = Json(1) })], errors);

        var error = Assert.Single(errors);
        Assert.Equal("MissingRequiredParameter", error.Code);
        Assert.Contains("eventKey", error.Message);
    }

    [Theory]
    [InlineData("october2026")]
    [InlineData("October26")]
    [InlineData("October2026Week1")]
    public void ShopEventWeekWithMalformedEventKeyFailsValidation(string eventKey)
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents(
            [ShopEventDefinition()],
            [ShopEventWeek(new() { ["weekNumber"] = Json(1), ["eventKey"] = Json(eventKey) })],
            errors);

        var error = Assert.Single(errors);
        Assert.Equal("InvalidShopEventKey", error.Code);
        Assert.Contains("occ-shop", error.Message);
    }

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("1.5")]
    [InlineData("0")]
    public void ShopEventWeekWithMalformedWeekNumberFailsValidation(string weekNumber)
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents(
            [ShopEventDefinition()],
            [ShopEventWeek(new()
            {
                ["weekNumber"] = JsonDocument.Parse(weekNumber).RootElement.Clone(),
                ["eventKey"] = Json("August2026"),
            })],
            errors);

        var error = Assert.Single(errors);
        Assert.Equal("InvalidShopEventWeek", error.Code);
        Assert.Contains("occ-shop", error.Message);
    }

    [Theory]
    [InlineData("incomeFree")]
    [InlineData("incomeBonusShipment")]
    public void ShopEventWeekWithNegativeIncomeFailsValidation(string parameter)
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents(
            [ShopEventDefinition()],
            [ShopEventWeek(new() { ["weekNumber"] = Json(1), ["eventKey"] = Json("August2026"), [parameter] = Json(-1) })],
            errors);

        var error = Assert.Single(errors);
        Assert.Equal("InvalidShopEventIncome", error.Code);
        Assert.Contains(parameter, error.Message);
    }

    [Fact]
    public void ShopEventWeekWithKeyAndIncomePasses()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents(
            [ShopEventDefinition()],
            [ShopEventWeek(new()
            {
                ["weekNumber"] = Json(1),
                ["eventKey"] = Json("August2026"),
                ["incomeFree"] = Json(750),
                ["incomeBonusShipment"] = Json(1000),
            })],
            errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void IncomeParametersAreIgnoredOnNonShopEvents()
    {
        var definition = Definition("hse-faction-focus");
        var occurrence = Occurrence("occ-focus", "hse-faction-focus",
            new Dictionary<string, JsonElement> { ["incomeFree"] = Json("not a number") });
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [occurrence], errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void OccurrenceWithStartBeforeEndPassesTimeWindowValidation()
    {
        var definition = Definition("hse-faction-focus");
        var occurrence = Occurrence("occ-valid", "hse-faction-focus");
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEvents([definition], [occurrence], errors);

        Assert.DoesNotContain(errors, error => error.Code == "InvalidTimeWindow");
    }
}
