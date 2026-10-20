using Avala.Agents.Contracts.Events;
using Avala.Testing;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ToolGroupViewModelScripts
{
    [Fact]
    public void AFoldedRunIsFoundByWhatItsToolsSayAndOnceUnfoldedLeavesThatToThem()
    {
        var toggled = 0;
        var group = new ToolGroupViewModel();
        group.Toggled += (_, _) => toggled++;
        group.Show(new ToolRun(ItemKind.Command, "4 commands", "go test ./...", "Run go test\nrefunds pass"));
        var folded = group.SearchText;

        ViewModelScript.Given(group)
            .Invoke(nameof(ToolGroupViewModel.ToggleCommand))
            .ThenNotified(nameof(ToolGroupViewModel.IsExpanded))
            .Then(unfolded => Assert.Equal(("Run go test\nrefunds pass", string.Empty, true, 1), (folded, unfolded.SearchText, unfolded.IsExpanded, toggled)));
    }

    [Fact]
    public void ARunIsRunningWhileOneOfItsToolsIs() =>
        ViewModelScript.Given(new ToolGroupViewModel())
            .When(group => group.Show(new ToolRun(ItemKind.Command, "4 commands", "go test ./...", string.Empty)))
            .ThenNotified(nameof(ToolGroupViewModel.IsRunning))
            .When(group => group.Show(new ToolRun(ItemKind.Command, "4 commands", string.Empty, string.Empty)))
            .Then(group => Assert.Equal((false, string.Empty), (group.IsRunning, group.Current)));
}
