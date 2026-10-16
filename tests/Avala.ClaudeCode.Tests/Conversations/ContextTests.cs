using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class ContextTests
{
    private const string Model = "claude-opus-5-5";

    [Fact]
    public void TheResultReportsHowMuchOfItsModelsWindowTheTurnsLastMessageFilledBeforeTheTurnEnds()
    {
        var talk = new Talk().Begin().Receive(Said(6, 51_545, 7_755), Result());

        var reported = Assert.Single(talk.Events.OfType<ContextReported>());
        Assert.Equal(new ContextReported(talk.Session, talk.Turn, 59_306, 1_000_000) { CompactsAt = 250_000L }, reported);
        Assert.True(talk.Events.IndexOf(reported) < talk.Events.FindIndex(happened => happened is TurnCompleted));
    }

    [Fact]
    public void ASubAgentsMessagesLeaveTheContextOfTheConversationAlone()
    {
        var talk = new Talk().Begin().Receive(Said(6, 51_545, 7_755), Said(2, 900_000, 0, parent: "toolu_1"), Result());

        Assert.Equal(59_306, Assert.Single(talk.Events.OfType<ContextReported>()).Used);
    }

    [Fact]
    public void ACompactionDuringTheTurnIsReportedWithTheContextLeftAfterIt()
    {
        var talk = new Talk().Begin().Receive(
            Said(6, 880_000, 20_000),
            Cli.Parse("""{ "type": "system", "subtype": "compact_boundary", "compact_metadata": { "trigger": "auto", "pre_tokens": 900006 } }"""),
            Said(4, 0, 31_000),
            Result());

        var reported = Assert.Single(talk.Events.OfType<ContextReported>());
        Assert.Equal((31_004L, true), (reported.Used, reported.Compacted));
    }

    [Theory]
    [InlineData("120000", "true", 120_000L)]
    [InlineData(null, null, 250_000L)]
    [InlineData("120000", "false", null)]
    public void TheContextSaysWhereItsConnectionCompactsAndNowhereWhenAutoCompactionIsOff(string? compactAt, string? autoCompact, long? compactsAt)
    {
        var settings = new Dictionary<string, string>();

        if (compactAt is not null)
        {
            settings["compactAt"] = compactAt;
        }

        if (autoCompact is not null)
        {
            settings["autoCompact"] = autoCompact;
        }

        var talk = new Talk(connection: new Avala.Agents.Contracts.Connections.ConnectionEnvironment { Settings = settings }).Begin().Receive(Said(6, 51_545, 7_755), Result());

        Assert.Equal(compactsAt is { } at ? Avala.Sdk.Option<long>.Some(at) : Avala.Sdk.Option<long>.None, Assert.Single(talk.Events.OfType<ContextReported>()).CompactsAt);
    }

    [Fact]
    public void AResultThatNamesNoContextWindowReportsNoContext()
    {
        var talk = new Talk().Begin().Receive(Said(6, 51_545, 7_755), Cli.Result(0.001m));

        Assert.Empty(talk.Events.OfType<ContextReported>());
    }

    private static JsonObject Said(long input, long cacheRead, long cacheWrite, string? parent = null) =>
        new JsonObject
        {
            ["type"] = "assistant",
            ["parent_tool_use_id"] = parent,
            ["message"] = new JsonObject
            {
                ["id"] = $"msg_{Guid.NewGuid():N}",
                ["model"] = Model,
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Working." }),
                ["usage"] = new JsonObject { ["input_tokens"] = input, ["cache_read_input_tokens"] = cacheRead, ["cache_creation_input_tokens"] = cacheWrite, ["output_tokens"] = 40 },
            },
        };

    private static JsonNode Result()
    {
        var result = Cli.Result(0.002m);
        result["modelUsage"] = Cli.Parse($$"""{ "{{Model}}": { "contextWindow": 1000000, "maxOutputTokens": 128000 } }""");

        return result;
    }
}
