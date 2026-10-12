using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class FileChangesViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AFilesHunksAreDrawnUnderItsPathAsync() =>
        ui.RunAsync(async () =>
        {
            var worktree = new Worktree { Changes = new FakeChanges { Hunks = { ["src/money.ts"] = Tools.Diff("src/money.ts") } } };
            var worked = worktree.Worked();
            var changes = new FileChangesViewModel(worktree.Job, "src/money.ts", worked);
            await changes.ShowCommand.ExecuteAsync(null);

            var view = Screen.Show(changes);

            Assert.Equal("src/money.ts", view.TextOf("Path"));
            Assert.Single(view.All<UserControl>(), control => control.GetType().Name == "HunkView");
            Assert.Contains("+    return units * pow10(exponent)", view.VisibleTexts);
            Assert.False(view.Shows("Note"));
        }, TestContext.Current.CancellationToken);
}
