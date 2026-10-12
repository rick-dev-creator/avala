using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ToolViewModelScripts
{
    [Fact]
    public void ARunningToolIsOneCollapsedLineThatExpandsToItsOutput() =>
        ViewModelScript.Given(new ToolViewModel(Running()))
            .Then(tool => Assert.Equal((ItemKind.Command, "npm test -- money.test.ts", "running", true, false, false), (tool.Kind, tool.Title, tool.Outcome, tool.IsRunning, tool.Failed, tool.IsExpanded)))
            .Invoke(nameof(ToolViewModel.ToggleCommand))
            .ThenNotified(nameof(ToolViewModel.IsExpanded))
            .Then(tool => Assert.True(tool.IsExpanded))
            .Invoke(nameof(ToolViewModel.ToggleCommand))
            .Then(tool => Assert.False(tool.IsExpanded));

    [Theory]
    [InlineData("Write sistema-solar.html", 4300, "Writing sistema-solar.html · 4.2 KB")]
    [InlineData("Edit src/money.ts", 812, "Editing src/money.ts · 812 B")]
    [InlineData("Write assets/atlas.svg", 2_200_000, "Writing assets/atlas.svg · 2.1 MB")]
    public void ADraftedFileEditIsRunningAndGrowsItsSizeUntilItStartsWithItsInput(string title, int received, string drafting) =>
        ViewModelScript.Given(new ToolViewModel(new ToolEntry("t", ItemKind.FileEdit, title, string.Empty, Option<ItemOutcome>.None) { Drafted = received }))
            .Then(tool => Assert.Equal((drafting, true, string.Empty), (tool.Title, tool.IsRunning, tool.Input)))
            .When(tool => tool.Update(new ToolEntry("t", ItemKind.FileEdit, title, string.Empty, Option<ItemOutcome>.None) { Input = "<html>" }))
            .ThenNotified(nameof(ToolViewModel.Title), nameof(ToolViewModel.Input))
            .Then(tool => Assert.Equal((title, true, "<html>"), (tool.Title, tool.IsRunning, tool.Input)));

    [Theory]
    [InlineData("Succeeded", "done", false)]
    [InlineData("Failed", "failed", true)]
    [InlineData("Cancelled", "cancelled", true)]
    [InlineData("Abandoned", "abandoned", true)]
    [InlineData("Expired", "expired", true)]
    public void AFinishedToolSaysHowItEnded(string outcome, string phrase, bool failed) =>
        ViewModelScript.Given(new ToolViewModel(Running()))
            .When(tool => tool.Update(Running() with { Output = "PASS", Outcome = Enum.Parse<ItemOutcome>(outcome) }))
            .ThenNotified(nameof(ToolViewModel.Outcome), nameof(ToolViewModel.IsRunning))
            .Then(tool => Assert.Equal((phrase, failed, false, "PASS"), (tool.Outcome, tool.Failed, tool.IsRunning, tool.Output)));

    [Fact]
    public void ADeniedToolSaysItWasDeniedAndIsNoFailure() =>
        ViewModelScript.Given(new ToolViewModel(Running()))
            .When(tool => tool.Update(Running() with { Outcome = ItemOutcome.Cancelled, Denied = true }))
            .ThenNotified(nameof(ToolViewModel.Outcome), nameof(ToolViewModel.ShowsOutcome))
            .Then(tool => Assert.Equal(("denied", false, true), (tool.Outcome, tool.Failed, tool.ShowsOutcome)));

    [Fact]
    public void AnExpandedToolStaysExpandedWhileItsOutputStreams() =>
        ViewModelScript.Given(new ToolViewModel(Running()))
            .Invoke(nameof(ToolViewModel.ToggleCommand))
            .When(tool => tool.Update(Running() with { Output = "PASS src/money" }))
            .Then(tool => Assert.Equal((true, "PASS src/money"), (tool.IsExpanded, tool.Output)));

    [Fact]
    public void AHarnessToolShowsTheInputItWasCalledWith() =>
        ViewModelScript.Given(new ToolViewModel(Running() with { Input = """{ "instruction": "Write the login tests" }""" }))
            .Then(tool => Assert.Equal("""{ "instruction": "Write the login tests" }""", tool.Input));

    [Theory]
    [InlineData("Started", "Delegated to Extract the address step · job started")]
    [InlineData("Reported", "Delegated to Extract the address step · reported its findings")]
    [InlineData("Refused", "Delegation refused · too many children")]
    public void ADelegateCallReadsAsWhatItDelegatedAndHowThatWent(string state, string title) =>
        ViewModelScript.Given(new ToolViewModel(Delegating()))
            .When(tool => tool.Update(Delegating() with { Delegation = Delegation(state) }))
            .ThenNotified(nameof(ToolViewModel.Title))
            .Then(tool => Assert.Equal(title, tool.Title));

    [Theory]
    [InlineData("Started", false, "Waiting for Extract the address step…")]
    [InlineData("Reported", true, "Waited for Extract the address step · reported its findings")]
    [InlineData("Unknown", false, "Waiting for a sub-agent…")]
    public void AWaitForASubAgentReadsAsWhoItWaitsForAndHowThatWent(string state, bool ended, string title) =>
        ViewModelScript.Given(new ToolViewModel(Waiting()))
            .When(tool => tool.Update(Waiting() with
            {
                Awaited = state == "Unknown" ? Option<DelegationRecord>.None : Delegation(state),
                Outcome = ended ? ItemOutcome.Succeeded : Option<ItemOutcome>.None,
            }))
            .Then(tool => Assert.Equal(title, tool.Title));

    private static ToolEntry Waiting() =>
        new("item:3", ItemKind.Other, "wait_child", string.Empty, Option<ItemOutcome>.None) { Input = """{"child":"7"}""", WaitsFor = JobId.New() };

    private static ToolEntry Delegating() =>
        new("item:2", ItemKind.Other, "delegate", string.Empty, Option<ItemOutcome>.None) { Input = """{"instruction":"Extract the address step"}""" };

    private static DelegationRecord Delegation(string state)
    {
        var child = JobId.New();
        var record = new DelegationRecord(SessionId.New(), new ItemId("delegate-1"), "Extract the address step\nKeep the tests green", DateTimeOffset.UnixEpoch) { Child = child };

        return state switch
        {
            "Reported" => record with { Report = new ChildReport(child, ChildOutcome.Reported, JobStatus.Discarded, DateTimeOffset.UnixEpoch) },
            "Refused" => record with { Child = Option<JobId>.None, Refusal = DelegationError.TooManyChildren },
            _ => record,
        };
    }

    private static ToolEntry Running() => new("item:1", ItemKind.Command, "npm test -- money.test.ts", string.Empty, Option<ItemOutcome>.None);
}
