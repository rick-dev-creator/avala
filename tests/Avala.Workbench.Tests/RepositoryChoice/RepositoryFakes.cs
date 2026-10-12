using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Submitting;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.RepositoryChoice;

internal sealed class FakeFolderPicker : IFolderPicker
{
    public Option<string> Answer { get; set; }

    public List<(string Title, string Near)> Asked { get; } = [];

    public ValueTask<Option<string>> PickAsync(string title, string near, CancellationToken cancellationToken)
    {
        Asked.Add((title, near));

        return ValueTask.FromResult(Answer);
    }
}

internal sealed class FakeRepositories : IRepositories
{
    public Dictionary<string, RepositoryState> States { get; } = [];

    public Option<WorkspaceFailure> Refusal { get; set; }

    public List<string> Initialized { get; } = [];

    public ValueTask<RepositoryState> InspectAsync(string folder, CancellationToken cancellationToken) =>
        ValueTask.FromResult(States.GetValueOrDefault(folder.Trim(), RepositoryState.Ready));

    public ValueTask<Result<string, WorkspaceFailure>> InitializeAsync(string folder, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Refusal.Match<Result<string, WorkspaceFailure>>(
            failure => failure,
            () =>
            {
                Initialized.Add(folder);
                States[folder] = RepositoryState.Ready;

                return folder;
            }));

    public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string parent, string name, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Refusal.Match<Result<string, WorkspaceFailure>>(failure => failure, () => $"{parent}/{name.Trim()}"));
}

internal static class Choosing
{
    public static RepositoryFieldViewModel Field() => Field(new JobBoard(), new FakeFolderPicker(), new FakeRepositories());

    public static RepositoryFieldViewModel Field(JobBoard board) => Field(board, new FakeFolderPicker(), new FakeRepositories());

    public static RepositoryFieldViewModel Field(JobBoard board, FakeFolderPicker picker, FakeRepositories repositories) =>
        new(picker, repositories, new RecentRepositories(board, Scratching.Folders()), new NewRepositoryViewModel(picker, repositories));
}

internal sealed class FakeScratch : IScratchRepositories
{
    public string Folder => "/data/scratch";

    public Option<WorkspaceFailure> Refusal { get; set; }

    public List<string> Instructions { get; } = [];

    public bool Holds(string repository) => repository.StartsWith($"{Folder}/", StringComparison.Ordinal);

    public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken)
    {
        Instructions.Add(instruction);

        return ValueTask.FromResult(Refusal.Match<Result<string, WorkspaceFailure>>(failure => failure, () => $"{Folder}/2026-10-12-scratch-{Instructions.Count}"));
    }
}

internal static class Scratching
{
    public static ScratchFolders Folders() => Folders(new FakeScratch(), new FakeOpener());

    public static ScratchFolders Folders(FakeScratch scratch) => Folders(scratch, new FakeOpener());

    public static ScratchFolders Folders(FakeScratch scratch, FakeOpener opener) => new(scratch, opener);
}
