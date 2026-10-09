using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Review;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Review;

public sealed class ChangedFileViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    [Fact]
    public async Task AFileShowsItsHunksOnDemandAndHidesThemAgain()
    {
        bench.Changes.Hunks["src/auth/login.ts"] = Diff(binary: false);
        var file = File(new FileChange("src/auth/login.ts", ChangeKind.Modified, 24, 3));
        var before = (file.IsExpanded, file.Hunks.Count, file.Counts);

        await file.ShowHunksCommand.ExecuteAsync(null);
        var shown = (file.IsExpanded, file.Hunks.Count);
        await file.ShowHunksCommand.ExecuteAsync(null);

        Assert.Equal((false, 0, "+24 -3"), before);
        Assert.Equal((true, 1), shown);
        Assert.Equal((false, 0), (file.IsExpanded, file.Hunks.Count));
    }

    [Fact]
    public async Task AFileWhoseChangesCannotBeReadSaysSoAndStaysCollapsed()
    {
        var file = File(new FileChange("src/auth/gone.ts", ChangeKind.Deleted, 0, 40));

        await file.ShowHunksCommand.ExecuteAsync(null);

        Assert.Equal(("The changes of this file could not be read.", false), (file.Error, file.IsExpanded));
    }

    [Fact]
    public async Task ABinaryFileSaysSoInsteadOfLines()
    {
        bench.Changes.Hunks["assets/logo.png"] = Diff(binary: true) with { Hunks = [] };
        var file = File(new FileChange("assets/logo.png", ChangeKind.Added, Option<int>.None, Option<int>.None));

        await file.ShowHunksCommand.ExecuteAsync(null);

        Assert.Equal(("binary", "A binary file", true), (file.Counts, file.Error, file.IsExpanded));
    }

    [Fact]
    public async Task ShowingHunksNotifiesTheViewThatTheFileExpanded()
    {
        bench.Changes.Hunks["src/auth/login.ts"] = Diff(binary: false);
        var script = ViewModelScript.Given(File(new FileChange("src/auth/login.ts", ChangeKind.Modified, 24, 3)));

        await script.ViewModel.ShowHunksCommand.ExecuteAsync(null);

        script.ThenNotified(nameof(ChangedFileViewModel.IsExpanded))
            .Then(file => Assert.True(file.IsExpanded));
    }

    public void Dispose() => bench.Dispose();

    private static FileDiff Diff(bool binary) =>
        new("src/auth/login.ts", binary, [new DiffHunk(12, 6, 12, 14, "export async function login", [new DiffLine(DiffLineKind.Added, "  limiter.consume();")])]);

    private ChangedFileViewModel File(FileChange change) => new(change, new WorkspaceId(Guid.NewGuid()), bench.Reader);
}
