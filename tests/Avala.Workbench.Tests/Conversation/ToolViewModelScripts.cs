using Avala.Agents.Contracts.Events;
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
    public void AnExpandedToolStaysExpandedWhileItsOutputStreams() =>
        ViewModelScript.Given(new ToolViewModel(Running()))
            .Invoke(nameof(ToolViewModel.ToggleCommand))
            .When(tool => tool.Update(Running() with { Output = "PASS src/money" }))
            .Then(tool => Assert.Equal((true, "PASS src/money"), (tool.IsExpanded, tool.Output)));

    [Fact]
    public void AHarnessToolShowsTheInputItWasCalledWith() =>
        ViewModelScript.Given(new ToolViewModel(Running() with { Input = """{ "instruction": "Write the login tests" }""" }))
            .Then(tool => Assert.Equal("""{ "instruction": "Write the login tests" }""", tool.Input));

    private static ToolEntry Running() => new("item:1", ItemKind.Command, "npm test -- money.test.ts", string.Empty, Option<ItemOutcome>.None);
}
