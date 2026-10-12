using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class JobHarnessScripts
{
    private static readonly ConnectionName Work = new("claude-work");

    private static readonly ConnectionName Personal = new("claude-personal");

    private readonly FakeJobs jobs = new();
    private readonly FakeHandoffRequests handoffs = new();
    private readonly FakeConnections connections = new("claude-work", "claude-personal");

    [Fact]
    public async Task TheChipReadsTheConnectionModelAndEffortTheJobRunsWithAsync()
    {
        var harness = await HarnessAsync(JobStatus.Running);

        Assert.Equal((true, "claude-work · claude-opus-5-5 · high", "CW"), (harness.Chip.IsShown, harness.Chip.Label, harness.Chip.Monogram));
        Assert.Equal("Runs on claude-work · no usage reported yet", harness.Chip.Reason);
    }

    [Fact]
    public async Task PickingAModelAndEffortForAHeldJobChoosesThemForItsNextMessageAsync()
    {
        var harness = await HarnessAsync(JobStatus.NeedsHelp);

        harness.Chip.Models.Model = "opus";
        await harness.Choosing;
        harness.Chip.Models.Effort = "high";
        await harness.Choosing;

        Assert.Equal(["choose opus · default", "choose opus · high"], jobs.Calls);
        Assert.Equal((true, JobHarnessPhrases.NextMessage), (harness.Chip.Models.CanChoose, harness.Chip.Models.Note));
    }

    [Fact]
    public async Task WhileTheAgentWorksTheModelAndTheConnectionAreShownButCannotChangeAsync()
    {
        var harness = await HarnessAsync(JobStatus.Running);

        Assert.Equal((false, "claude-opus-5-5", "high", "Change it once the agent stops."), (harness.Chip.Models.CanChoose, harness.Chip.Models.Model, harness.Chip.Models.Effort, harness.Chip.Models.Note));
        Assert.Equal([("claude-work", true, true), ("Continue on claude-personal", false, false)], harness.Chip.Options.Select(option => (option.Name, option.IsChosen, option.IsAvailable)));
        Assert.Equal("Stop the agent to continue on another connection.", harness.Chip.Note);
        Assert.Empty(jobs.Calls);
    }

    [Fact]
    public async Task ContinuingAHeldJobOnAnotherConnectionHandsItOffThereAndClosesTheMenuAsync()
    {
        var harness = await HarnessAsync(JobStatus.NeedsHelp);
        harness.Chip.IsOpen = true;
        await harness.Loading;

        harness.Chip.Options.Single(option => option.Name == "Continue on claude-personal").ChooseCommand.Execute(null);
        await harness.Moving;

        Assert.Equal([(harness.Job, Personal)], handoffs.Requests);
        Assert.False(harness.Chip.IsOpen);
        Assert.Equal("Continuing on another connection starts a new conversation there, with a brief of the work so far.", harness.Chip.Note);
    }

    [Fact]
    public async Task ARefusedHandoffSaysWhyInTheMenuAsync()
    {
        var harness = await HarnessAsync(JobStatus.NeedsHelp);
        handoffs.Refusal = JobRejection.UnusableConnection;
        harness.Chip.IsOpen = true;
        await harness.Loading;

        harness.Chip.Options.Single(option => option.Name == "Continue on claude-personal").ChooseCommand.Execute(null);
        await harness.Moving;

        Assert.Equal((true, "The job's connection cannot be used."), (harness.Chip.IsOpen, harness.Chip.Note));
    }

    private async Task<JobHarness> HarnessAsync(JobStatus status)
    {
        connections.Offers[Work.Value] = CapabilitySet.None.With(new Resumable(), new OffersModels(["sonnet", "opus"], ["low", "high"]) { DefaultModel = "sonnet" });
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary with { Connection = Work };
        var ran = Transcript.Empty.Apply(new ModelReported(SessionId.New(), TurnId.New(), "claude-opus-5-5") { Effort = "high" }, DateTimeOffset.UnixEpoch);
        var harness = Composing.Harnesses(jobs, handoffs, connections).Open(summary.Job);
        harness.Track(new BoardJob(summary, ran));
        await harness.Loading;

        return harness;
    }
}
