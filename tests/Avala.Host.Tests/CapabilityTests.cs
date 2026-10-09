using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class CapabilityTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("", new[] { "Hello! ", "This is a simulated ", "Claude Code turn." })]
    [InlineData("streamsPartialOutput", new[] { "Hello! This is a simulated Claude Code turn." })]
    public async Task AReplyStreamsInPartsOnlyOnAConnectionThatStreamsPartialOutputAsync(string without, string[] parts)
    {
        await using var run = await WithoutAsync("reply", without);

        var turn = await run.TurnAsync();

        Assert.Equal(parts, turn.OfType<ItemProgressed>().Where(progressed => progressed.Item == new ItemId("reply")).Select(progressed => progressed.Text));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("exposesReasoning", false)]
    public async Task ReasoningShowsOnlyOnAConnectionThatExposesItAsync(string without, bool shown)
    {
        await using var run = await WithoutAsync("reply", without);

        var turn = await run.TurnAsync();

        Assert.Equal(shown, turn.OfType<ItemStarted>().Any(started => started.Kind == ItemKind.Reasoning));
    }

    [Theory]
    [InlineData("", true, true)]
    [InlineData("reportsCost", true, false)]
    [InlineData("reportsUsage", false, false)]
    public async Task UsageAndItsCostAreReportedOnlyOnAConnectionThatReportsThemAsync(string without, bool usage, bool cost)
    {
        await using var run = await WithoutAsync("reply", without);

        var turn = await run.TurnAsync();

        Assert.Equal(usage, turn.OfType<UsageReported>().Any());
        Assert.Equal(cost, turn.OfType<UsageReported>().Any(reported => reported.Cost.IsSome));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("reportsLimits", false)]
    public async Task LimitsAreReportedOnlyOnAConnectionThatReportsThemAsync(string without, bool reported)
    {
        await using var run = await WithoutAsync("reply", without);

        var turn = await run.TurnAsync();

        Assert.Equal(reported, turn.OfType<LimitReported>().Any());
    }

    [Fact]
    public async Task ALoginReportsItsLimitWindowsAndAnApiKeyConnectionReportsNoneAsync()
    {
        var variable = $"AVALA_TEST_KEY_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "sk-simulated");

        try
        {
            await using var run = await SimulatedRun.ConnectedAsync(
                plugins,
                "near-limit",
                new ConnectionName("login"),
                [
                    ("connections.json", $$"""
                        {
                          "connections": [
                            { "name": "login", "provider": "simulator", "credential": { "source": "login" } },
                            { "name": "key", "provider": "simulator", "credential": { "source": "apiKey", "reference": "{{variable}}" } }
                          ]
                        }
                        """),
                    ("connections/login/.login", string.Empty),
                ]);
            var onLogin = await run.TurnAsync();
            var keyed = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("near-limit")) { Connection = new ConnectionName("key") }));

            Assert.Equal([JobStatus.AwaitingReview, JobStatus.AwaitingReview], await run.SettledAsync(run.Job, keyed));
            var onKey = await run.TurnAsync();
            Assert.Equal(["5h"], onLogin.OfType<LimitReported>().Select(limit => limit.Limit.Window));
            Assert.Empty(onKey.OfType<LimitReported>());
            Assert.Contains(onKey, agentEvent => agentEvent is UsageReported { Cost.IsSome: true });
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("resumable", false)]
    public async Task AConversationCanBeResumedOnlyOnAConnectionThatIsResumableAsync(string without, bool resumable)
    {
        await using var run = await WithoutAsync("reply", without);

        var turn = await run.TurnAsync();

        Assert.Equal(resumable, turn.OfType<ResumeTokenIssued>().Any());
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("acceptsTools", false)]
    public async Task AHarnessToolIsCalledOnlyOnAConnectionThatAcceptsToolsAsync(string without, bool called)
    {
        await using var run = await WithoutAsync("follow-up", without);

        var turn = await run.TurnAsync();

        Assert.Equal(called, turn.OfType<ToolCalled>().Any());
        Assert.Equal(!called, turn.OfType<ItemProgressed>().Any(progressed => progressed.Text.StartsWith("I would call propose_follow_up", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ACanvasIsDrawnAsAMessageOnAConnectionThatAcceptsOnlyExecutedToolsAsync()
    {
        await using var run = await OnAsync("canvas", """ "toolSurfaces": "executed" """);

        var turn = await run.TurnAsync();

        Assert.Empty(turn.OfType<CanvasStarted>());
        Assert.Contains(turn, agentEvent => agentEvent is ItemStarted { Kind: ItemKind.Message });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AQuestionIsNotAskedOnAConnectionThatAsksNoFormsAsync()
    {
        await using var run = await WithoutAsync("question", "asksForms");

        var turn = await run.TurnAsync();

        Assert.Empty(turn.OfType<FormRequested>());
        Assert.Contains(turn, agentEvent => agentEvent is ItemProgressed progressed && progressed.Text.StartsWith("I would ask: ", StringComparison.Ordinal));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("interruptible", false)]
    public async Task ARunningTurnIsInterruptedOnlyOnAConnectionThatIsInterruptibleAsync(string without, bool interrupted)
    {
        await using var run = await WithoutAsync("hang", without);
        var opened = await run.OpenedAsync();
        await run.ResumableAsync();

        var interruption = await run.Get<IAgents>().InterruptAsync(opened.Session, Cancellation);

        if (interrupted)
        {
            Outcomes.Succeeds(interruption);
            Assert.Equal(TurnOutcome.Interrupted, Assert.IsType<TurnCompleted>((await run.TurnAsync())[^1]).Outcome);
        }
        else
        {
            Assert.Equal(AgentError.Unsupported, Outcomes.FailsWith(interruption));
        }
    }

    private Task<SimulatedRun> WithoutAsync(string scenario, string without) =>
        OnAsync(scenario, $""" "withoutCapabilities": "{without}" """);

    private Task<SimulatedRun> OnAsync(string scenario, string settings) =>
        SimulatedRun.ConnectedAsync(
            plugins,
            scenario,
            new ConnectionName("tailored"),
            [("connections.json", $$"""{ "connections": [ { "name": "tailored", "provider": "simulator", "settings": { {{settings}} } } ] }""")]);
}
