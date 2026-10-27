using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>Covers <c>fix-legendary-event-run-and-goal</c>'s <c>legendary-event-run-schedule</c> capability: the
/// served <c>eventStageStartDatesUtc</c> is the raw <c>runStartDatesUtc</c> normalised to ISO 8601 UTC with the
/// index as run - 1, no derived run or lifecycle field (run number, next run, finished) is served, and the
/// loader rejects malformed schedules.</summary>
public sealed class LreRunScheduleTests
{
    private static readonly GameCatalogSnapshot Snapshot = GameCatalogLoader.Load();

    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static GameCatalogLre Raw(string id) => Snapshot.Lres.Single(lre => lre.UnitSnowprintId == id);

    private static GameCatalogLreView Serve(GameCatalogLre raw) =>
        GameCatalogDenormalizer.BuildLres(
                new Dictionary<string, GameCatalogLre>(StringComparer.Ordinal) { ["lres-test"] = raw },
                Snapshot.UnitsByFaction)
            .Single();

    private static List<GameCatalogValidationError> Validate(params string[] starts)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateLreRunStarts([Raw("votanUthar") with { RunStartDatesUtc = starts }], errors);
        return errors;
    }

    [Theory]
    [InlineData("votanUthar", new[] { "2026-03-08T00:00:00Z", "2026-10-04T00:00:00Z" })]
    [InlineData("tauFarsight", new[] { "2025-12-28T00:00:00Z", "2026-06-21T00:00:00Z" })]
    [InlineData("astarLysander", new[] { "2026-08-30T00:00:00Z" })]
    [InlineData("lostaBile", new[] { "2026-11-08T00:00:00Z" })]
    public void EmbeddedEventsServeTheirAnnouncedRunsInRunOrder(string id, string[] expected)
    {
        var served = Snapshot.LreViews.Single(view => view.Id == id).EventStageStartDatesUtc;

        Assert.Equal(expected, served);
        var instants = served.Select(start => DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.All(instants.Zip(instants.Skip(1)), pair => Assert.True(pair.Second - pair.First >= TimeSpan.FromDays(7)));
    }

    [Fact]
    public void TheShippedSchedulesAreValid()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreRunStarts(Snapshot.Lres, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void ATwoEntryScheduleServesTwoIsoStringsInOrder()
    {
        var raw = Raw("votanUthar") with
        {
            RunStartDatesUtc = ["Sun, 08 March 2026 00:00:00 GMT", "2026-10-04T00:00:00Z"],
        };

        Assert.Equal(["2026-03-08T00:00:00Z", "2026-10-04T00:00:00Z"], Serve(raw).EventStageStartDatesUtc);
    }

    [Fact]
    public void AnEmptyScheduleServesAnEmptyArray()
    {
        Assert.Empty(Serve(Raw("votanUthar") with { RunStartDatesUtc = [] }).EventStageStartDatesUtc);
    }

    [Fact]
    public void AnAbsentFieldReadsAsEmpty()
    {
        var raw = System.Text.Json.JsonSerializer.Deserialize<GameCatalogLre>(
            """{ "unitSnowprintId": "votanUthar" }""",
            WebJson)!;

        Assert.NotNull(raw.RunStartDatesUtc);
        Assert.Empty(raw.RunStartDatesUtc);
        Assert.Empty(Serve(Raw("votanUthar") with { RunStartDatesUtc = raw.RunStartDatesUtc }).EventStageStartDatesUtc);
    }

    [Fact]
    public void TheCuratedEventsCarryNoDerivableScheduleFields()
    {
        var assembly = typeof(GameCatalogLoader).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".{GameCatalogDatasets.LreCuration}.json", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var document = System.Text.Json.JsonDocument.Parse(stream);
        var events = document.RootElement.EnumerateObject().ToArray();

        Assert.Equal(Snapshot.Lres.Count, events.Length);
        foreach (var lre in events)
        {
            var keys = lre.Value.EnumerateObject().Select(property => property.Name).ToArray();

            Assert.DoesNotContain(keys, key => key is "eventStage" or "nextEventDate" or "nextEventDateUtc" or "finished");
        }
    }

    [Fact]
    public void TheServedRecordCarriesNoDerivedRunField()
    {
        var properties = typeof(GameCatalogLreView).GetProperties().Select(property => property.Name).ToArray();

        Assert.Contains(nameof(GameCatalogLreView.EventStageStartDatesUtc), properties);
        // MaxRuns is the game's run count (heroEventMaxRuns), not state derived from the schedule.
        Assert.DoesNotContain(properties, name =>
            name != nameof(GameCatalogLreView.EventStageStartDatesUtc)
            && name != nameof(GameCatalogLreView.MaxRuns)
            && (name.Contains("Run", StringComparison.Ordinal)
                || name.Contains("Stage", StringComparison.Ordinal)
                || name.Contains("Lifecycle", StringComparison.Ordinal)
                || name.Contains("Next", StringComparison.Ordinal)
                || name.Contains("Finished", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sun, 08 March 2026 00:00:00 GMT")]
    [InlineData("Sun, 08 March 2026 00:00:00 GMT|Sun, 15 March 2026 00:00:00 GMT")]
    [InlineData("2026-03-08T00:00:00Z|2026-06-21T00:00:00Z|2026-10-04T00:00:00Z")]
    public void ZeroToThreeWellSpacedStartsPass(string starts)
    {
        Assert.Empty(Validate(starts.Split('|', StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void FourRunStartsFail()
    {
        var error = Assert.Single(Validate(
            "2026-01-04T00:00:00Z", "2026-03-08T00:00:00Z", "2026-06-21T00:00:00Z", "2026-10-04T00:00:00Z"));

        Assert.Equal((GameCatalogDatasets.LresPrefix, "InvalidRunStartDates"), (error.Dataset, error.Code));
        Assert.Contains("votanUthar", error.Message, StringComparison.Ordinal);
        Assert.Contains(Raw("votanUthar").SourceFile, error.Message, StringComparison.Ordinal);
        Assert.Contains("more than 3 run starts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunsFiveDaysApartFailAsOverlapping()
    {
        var error = Assert.Single(Validate("2026-03-08T00:00:00Z", "2026-03-13T00:00:00Z"));

        Assert.Equal("InvalidRunStartDates", error.Code);
        Assert.Contains("index 1", error.Message, StringComparison.Ordinal);
        Assert.Contains("run 2 starts 5 days after run 1, overlapping run 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnparseableEntryFailsNamingItsIndex()
    {
        var error = Assert.Single(Validate("2026-03-08T00:00:00Z", "soon"));

        Assert.Equal("InvalidRunStartDates", error.Code);
        Assert.Contains("index 1 ('soon')", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutOfOrderStartsFailNamingIndexOne()
    {
        var error = Assert.Single(Validate("2026-10-04T00:00:00Z", "2026-03-08T00:00:00Z"));

        Assert.Equal("InvalidRunStartDates", error.Code);
        Assert.Contains("index 1", error.Message, StringComparison.Ordinal);
        Assert.Contains("strictly ascending", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8 March")]
    [InlineData("12:00")]
    [InlineData("2026-03-08")]
    public void APartialDateFailsInsteadOfFillingFromTheClock(string partial)
    {
        var error = Assert.Single(Validate("2026-01-04T00:00:00Z", partial));

        Assert.Equal("InvalidRunStartDates", error.Code);
        Assert.Contains($"index 1 ('{partial}')", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCatalogValidatorRunsTheRunStartsCheck()
    {
        var raw = Raw("votanUthar");
        var broken = Snapshot.LresByEvent.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.UnitSnowprintId == "votanUthar" ? raw with { RunStartDatesUtc = ["soon"] } : pair.Value);

        var errors = GameCatalogValidator.Validate(Snapshot with { LresByEvent = broken });

        Assert.Contains(errors, error => error.Code == "InvalidRunStartDates" && error.Message.Contains("votanUthar", StringComparison.Ordinal));
    }
}
