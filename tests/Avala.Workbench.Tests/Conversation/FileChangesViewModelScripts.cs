using Avala.Workbench.Conversation;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Conversation;

public sealed class FileChangesViewModelScripts
{
    [Fact]
    public async Task ShowingReadsTheFilesHunksAgainEachTimeItOpensAsync()
    {
        var worktree = new Worktree { Changes = new FakeChanges { Hunks = { ["src/money.ts"] = Tools.Diff("src/money.ts") } } };
        var worked = worktree.Worked();
        var changes = new FileChangesViewModel(worktree.Job, "src/money.ts", worked);
        await changes.ShowCommand.ExecuteAsync(null);
        changes.IsOpen = false;

        worktree.Changes.Hunks["src/money.ts"] = new FileDiff("src/money.ts", false, [.. Tools.Diff("src/money.ts").Hunks, .. Tools.Diff("src/money.ts").Hunks]);
        await changes.ShowCommand.ExecuteAsync(null);

        Assert.Equal((true, 2, string.Empty), (changes.IsOpen, changes.Hunks.Count, changes.Note));
    }

    [Theory]
    [InlineData("src/money.ts", "No changes left in this file.")]
    [InlineData("assets/logo.png", "A binary file.")]
    [InlineData("src/gone.ts", "The changes of this file could not be read.")]
    public async Task AFileWhoseHunksCannotBeShownSaysWhyAsync(string path, string note)
    {
        var worktree = new Worktree
        {
            Changes = new FakeChanges
            {
                Hunks =
                {
                    ["src/money.ts"] = new FileDiff("src/money.ts", false, []),
                    ["assets/logo.png"] = new FileDiff("assets/logo.png", true, []),
                },
            },
        };
        var worked = worktree.Worked();
        var changes = new FileChangesViewModel(worktree.Job, path, worked);

        await changes.ShowCommand.ExecuteAsync(null);

        Assert.Equal((true, note), (changes.IsOpen, changes.Note));
    }
}
