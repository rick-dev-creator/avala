using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ToolViewModelScripts
{
    [Fact]
    public void ARunningToolIsOneCollapsedLineThatExpandsToItsOutput() =>
        ViewModelScript.Given(Tools.Of(Running()))
            .Then(tool => Assert.Equal((ItemKind.Command, "npm test -- money.test.ts", "running", true, false, false), (tool.Kind, tool.Title, tool.Outcome, tool.IsRunning, tool.Failed, tool.IsExpanded)))
            .Invoke(nameof(ToolViewModel.ToggleCommand))
            .ThenNotified(nameof(ToolViewModel.IsExpanded))
            .Then(tool => Assert.True(tool.IsExpanded))
            .Invoke(nameof(ToolViewModel.ToggleCommand))
            .Then(tool => Assert.False(tool.IsExpanded));

    [Fact]
    public void AFinishedCommandWithLongOutputShowsItsLastLinesUntilAllAreAskedFor()
    {
        var output = string.Join('\n', Enumerable.Range(1, 45).Select(line => $"line {line}"));

        ViewModelScript.Given(Tools.Of(Running() with { Output = output, Outcome = ItemOutcome.Succeeded }))
            .Then(tool => Assert.Equal((25, "line 26", true), (tool.HiddenLines, tool.ShownOutput.Split('\n')[0], tool.ShowAllOutputCommand.CanExecute(null))))
            .Invoke(nameof(ToolViewModel.ShowAllOutputCommand))
            .Then(tool => Assert.Equal((0, output), (tool.HiddenLines, tool.ShownOutput)));
    }

    [Fact]
    public void ARunningCommandStreamsItsWholeOutput()
    {
        var output = string.Join('\n', Enumerable.Range(1, 45).Select(line => $"line {line}"));

        ViewModelScript.Given(Tools.Of(Running() with { Output = output }))
            .Then(tool => Assert.Equal((0, output), (tool.HiddenLines, tool.ShownOutput)));
    }

    [Fact]
    public void AFailedCommandListsTheLinesThatLookLikeErrorsAndNotTheCleanSummaries() =>
        ViewModelScript.Given(Tools.Of(Running() with
            {
                Output = "Restoring\nerror CS0103: The name 'Money' does not exist\nTests: 3 passed, 0 failed\n--- FAIL: TestInvoiceTotal (0.00s)\nBuild FAILED.\nErrors: 0",
                Outcome = ItemOutcome.Failed,
            }))
            .Then(tool => Assert.Equal(["error CS0103: The name 'Money' does not exist", "--- FAIL: TestInvoiceTotal (0.00s)", "Build FAILED."], tool.Problems));

    [Fact]
    public void ASucceededCommandListsNoProblems() =>
        ViewModelScript.Given(Tools.Of(Running() with { Output = "warning: error handling deprecated", Outcome = ItemOutcome.Succeeded }))
            .Then(tool => Assert.Empty(tool.Problems));

    [Theory]
    [InlineData("Write sistema-solar.html", 4300, "Writing sistema-solar.html · 4.2 KB")]
    [InlineData("Edit src/money.ts", 812, "Editing src/money.ts · 812 B")]
    [InlineData("Write assets/atlas.svg", 2_200_000, "Writing assets/atlas.svg · 2.1 MB")]
    public void ADraftedFileEditIsRunningAndGrowsItsSizeUntilItStartsWithItsInput(string title, int received, string drafting) =>
        ViewModelScript.Given(Tools.Of(new ToolEntry("t", ItemKind.FileEdit, title, string.Empty, Option<ItemOutcome>.None) { Drafted = received }))
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
        ViewModelScript.Given(Tools.Of(Running()))
            .When(tool => tool.Update(Running() with { Output = "PASS", Outcome = Enum.Parse<ItemOutcome>(outcome) }))
            .ThenNotified(nameof(ToolViewModel.Outcome), nameof(ToolViewModel.IsRunning))
            .Then(tool => Assert.Equal((phrase, failed, false, "PASS"), (tool.Outcome, tool.Failed, tool.IsRunning, tool.Output)));

    [Fact]
    public void AStoppedSubagentSaysItWasStoppedAsTheTreeDoesAndIsNoFailure() =>
        ViewModelScript.Given(Tools.Of(Running() with { Kind = ItemKind.Subagent }))
            .When(tool => tool.Update(Running() with { Kind = ItemKind.Subagent, Outcome = ItemOutcome.Cancelled }))
            .Then(tool => Assert.Equal(("stopped", false, true), (tool.Outcome, tool.Failed, tool.ShowsOutcome)));

    [Fact]
    public void ASubagentWhoseLaunchWasDeniedSaysItWasDeniedNotStopped() =>
        ViewModelScript.Given(Tools.Of(Running() with { Kind = ItemKind.Subagent }))
            .When(tool => tool.Update(Running() with { Kind = ItemKind.Subagent, Outcome = ItemOutcome.Cancelled, Denied = true }))
            .Then(tool => Assert.Equal(("denied", false), (tool.Outcome, tool.Failed)));

    [Fact]
    public void ADeniedToolSaysItWasDeniedAndIsNoFailure() =>
        ViewModelScript.Given(Tools.Of(Running()))
            .When(tool => tool.Update(Running() with { Outcome = ItemOutcome.Cancelled, Denied = true }))
            .ThenNotified(nameof(ToolViewModel.Outcome), nameof(ToolViewModel.ShowsOutcome))
            .Then(tool => Assert.Equal(("denied", false, true), (tool.Outcome, tool.Failed, tool.ShowsOutcome)));

    [Fact]
    public void AnExpandedToolStaysExpandedWhileItsOutputStreams() =>
        ViewModelScript.Given(Tools.Of(Running()))
            .Invoke(nameof(ToolViewModel.ToggleCommand))
            .When(tool => tool.Update(Running() with { Output = "PASS src/money" }))
            .Then(tool => Assert.Equal((true, "PASS src/money"), (tool.IsExpanded, tool.Output)));

    [Fact]
    public void AHarnessToolShowsTheInputItWasCalledWith() =>
        ViewModelScript.Given(Tools.Of(Running() with { Input = """{ "instruction": "Write the login tests" }""" }))
            .Then(tool => Assert.Equal("""{ "instruction": "Write the login tests" }""", tool.Input));

    [Theory]
    [InlineData("Started", "Delegated to Extract the address step · job started")]
    [InlineData("Reported", "Delegated to Extract the address step · reported its findings")]
    [InlineData("Refused", "Delegation refused · too many children")]
    public void ADelegateCallReadsAsWhatItDelegatedAndHowThatWent(string state, string title) =>
        ViewModelScript.Given(Tools.Of(Delegating()))
            .When(tool => tool.Update(Delegating() with { Delegation = Delegation(state) }))
            .ThenNotified(nameof(ToolViewModel.Title))
            .Then(tool => Assert.Equal(title, tool.Title));

    [Theory]
    [InlineData("Started", false, "Waiting for Extract the address step…")]
    [InlineData("Reported", true, "Waited for Extract the address step · reported its findings")]
    [InlineData("Unknown", false, "Waiting for a sub-agent…")]
    public void AWaitForASubAgentReadsAsWhoItWaitsForAndHowThatWent(string state, bool ended, string title) =>
        ViewModelScript.Given(Tools.Of(Waiting()))
            .When(tool => tool.Update(Waiting() with
            {
                Awaited = state == "Unknown" ? Option<DelegationRecord>.None : Delegation(state),
                Outcome = ended ? ItemOutcome.Succeeded : Option<ItemOutcome>.None,
            }))
            .Then(tool => Assert.Equal(title, tool.Title));

    [Fact]
    public async Task CopyPutsTheToolsOutputOnTheClipboardAndConfirmsItAsync()
    {
        var clipboard = new FakeClipboard();
        var tool = Tools.Of(Running() with { Output = "PASS src/money", Outcome = ItemOutcome.Succeeded }, clipboard);

        await tool.CopyCommand.ExecuteAsync(null);

        Assert.Equal(["PASS src/money"], clipboard.Copied);
        Assert.True(tool.IsCopied);
    }

    [Fact]
    public async Task ClickingAnEditsLineExpandsToTheDiffOfThatEditAloneAsync()
    {
        var changes = new FakeChanges { Hunks = { ["src/money.ts"] = Tools.Diff("src/money.ts") } };
        var tool = Tools.InWorktree(new ToolEntry("t", ItemKind.FileEdit, "Edit src/money.ts", string.Empty, ItemOutcome.Succeeded) { Input = "- return units * 100\n+ return units * pow10(exponent)" }, changes);

        await tool.ToggleCommand.ExecuteAsync(null);

        Assert.Equal((true, true, false, false), (tool.IsExpanded, tool.ShowsEdit, tool.ShowsInput, tool.Changes.IsOpen));
        Assert.Equal([("-return units * 100", DiffLineKind.Removed), ("+return units * pow10(exponent)", DiffLineKind.Added)], tool.Edit.Lines.Select(line => (line.Text, line.Kind)));
    }

    [Fact]
    public async Task AnExpandedEditOpensEveryChangeToItsFileAsync()
    {
        var changes = new FakeChanges { Hunks = { ["src/money.ts"] = Tools.Diff("src/money.ts") } };
        var tool = Tools.InWorktree(new ToolEntry("t", ItemKind.FileEdit, "Edit src/money.ts", string.Empty, ItemOutcome.Succeeded) { Input = "- a\n+ b" }, changes);
        await tool.ToggleCommand.ExecuteAsync(null);

        await tool.ShowChangesCommand.ExecuteAsync(null);

        Assert.Equal((true, true), (tool.ShowsChanges, tool.Changes.IsOpen));
        Assert.Equal(["-    return units * 100", "+    return units * pow10(exponent)"], tool.Changes.Hunks.Single().Lines.Select(line => line.Text));
    }

    [Fact]
    public async Task AnEditWithoutItsInputOpensItsFilesChangesAsync()
    {
        var changes = new FakeChanges { Hunks = { ["src/money.ts"] = Tools.Diff("src/money.ts") } };
        var tool = Tools.InWorktree(new ToolEntry("t", ItemKind.FileEdit, "Edit src/money.ts", string.Empty, ItemOutcome.Succeeded), changes);

        await tool.ToggleCommand.ExecuteAsync(null);

        Assert.Equal((false, true), (tool.IsExpanded, tool.Changes.IsOpen));
    }

    [Fact]
    public async Task AFileEditStillBeingWrittenExpandsToItsInputAsync()
    {
        var tool = Tools.InWorktree(new ToolEntry("t", ItemKind.FileEdit, "Write src/money.ts", string.Empty, Option<ItemOutcome>.None) { Input = "export {}" }, new FakeChanges());

        await tool.ToggleCommand.ExecuteAsync(null);

        Assert.Equal((false, true, false), (tool.ShowsChanges, tool.IsExpanded, tool.Changes.IsOpen));
    }

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
