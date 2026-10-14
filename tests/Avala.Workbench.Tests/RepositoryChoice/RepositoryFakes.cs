using Avala.Forges.Contracts;
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

internal sealed class FakeRemotes : IRemoteRepositories
{
    public Option<WorkspaceFailure> Refusal { get; set; }

    public Option<TaskCompletionSource> Held { get; set; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<(string Url, string Parent, string Name)> Cloned { get; } = [];

    public Option<string> Origin { get; set; }

    public ValueTask<Option<string>> RemoteAsync(string repository, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(remote == "origin" ? Origin : Option<string>.None);

    public ValueTask<bool> HasCredentialHelperAsync(string repository, string url, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<Result<string, WorkspaceFailure>> PublishAsync(string repository, string remote, string url, CancellationToken cancellationToken) => throw new NotSupportedException();

    public async ValueTask<Result<string, WorkspaceFailure>> CloneAsync(string url, string parent, string name, CancellationToken cancellationToken)
    {
        Cloned.Add((url, parent, name));
        Started.TrySetResult();
        await Held.Match(held => held.Task.WaitAsync(cancellationToken), () => Task.CompletedTask);

        return Refusal.Match<Result<string, WorkspaceFailure>>(failure => failure, () => $"{parent}/{name.Trim()}");
    }
}

internal sealed class FakePublishing : IRepositoryPublishing
{
    public static PublishTarget GitHub { get; } = new(new ForgeName("github"), "GitHub");

    public List<PublishTarget> Targets { get; } = [GitHub];

    public Dictionary<string, RepositoryRemote> Remotes { get; } = [];

    public Option<ForgeError> Refusal { get; set; }

    public List<(string Repository, ForgeName Forge, RepositoryDraft Draft)> Published { get; } = [];

    public ValueTask<IReadOnlyList<PublishTarget>> TargetsAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<PublishTarget>>(Targets);

    public ValueTask<Option<RepositoryRemote>> RemoteOfAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Remotes.TryGetValue(repository, out var remote) ? remote : Option<RepositoryRemote>.None);

    public ValueTask<Result<PublishedRepository, ForgeError>> PublishAsync(string repository, ForgeName forge, RepositoryDraft draft, CancellationToken cancellationToken)
    {
        Published.Add((repository, forge, draft));

        return ValueTask.FromResult(Refusal.Match<Result<PublishedRepository, ForgeError>>(
            error => error,
            () =>
            {
                var address = new RepositoryAddress("github.com", "ada", draft.Name);
                var url = $"git@github.com:ada/{draft.Name}.git";
                var target = Targets.Find(found => found.Forge == forge) ?? GitHub;
                Remotes[repository] = new RepositoryRemote("origin", url) { Address = address, Forge = target };

                return new PublishedRepository(target, address, "main", url);
            }));
    }
}

internal static class Choosing
{
    public static RepositoryFieldViewModel Field() => Field(new JobBoard(), new FakeFolderPicker(), new FakeRepositories());

    public static RepositoryFieldViewModel Field(JobBoard board) => Field(board, new FakeFolderPicker(), new FakeRepositories());

    public static RepositoryFieldViewModel Field(JobBoard board, FakeFolderPicker picker, FakeRepositories repositories) =>
        Field(board, picker, repositories, new FakeRemotes());

    public static RepositoryFieldViewModel Field(JobBoard board, FakeFolderPicker picker, FakeRepositories repositories, FakeRemotes remotes) =>
        new(picker, repositories, new RecentRepositories(board, Scratching.Folders()), new RepositoryStarts(Creation(picker, repositories, new FakePublishing()), Cloning(new Accounts.FakeSourceControl(), remotes, new FakeProjects(), picker)));

    public static CloneSheetViewModel Cloning(Accounts.FakeSourceControl forges, FakeRemotes remotes, FakeProjects projects, FakeFolderPicker picker) =>
        new(new CloneServices(forges.Source, remotes, new ProjectsPlace(projects, picker), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now)), new Avala.Workbench.Accounts.ConnectSheetViewModel(forges.Source));

    public static DateTimeOffset Now { get; } = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    public static RepositoryPickerViewModel Picker() => Picker(new JobBoard());

    public static RepositoryPickerViewModel Picker(JobBoard board) => Picker(board, new FakeFolderPicker(), new FakeRepositories());

    public static RepositoryPickerViewModel Picker(JobBoard board, FakeFolderPicker picker, FakeRepositories repositories) =>
        new(Field(board, picker, repositories), new RecentRepositories(board, Scratching.Folders()), new ProjectsPlace(new FakeProjects(), picker));

    public static NewRepositoryViewModel Creation(FakeFolderPicker picker, FakeRepositories repositories, FakePublishing publishing) =>
        new(new ProjectsPlace(new FakeProjects(), picker), repositories, new PublishOptionsViewModel(publishing));
}

internal sealed class FakeProjects : IProjectsFolder
{
    public string Suggested { get; set; } = "/home/ada/Projects";

    public Option<string> Chosen { get; set; }

    public List<string> Choices { get; } = [];

    public ValueTask<ProjectsFolder> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new ProjectsFolder(Suggested, Chosen));

    public ValueTask<Result<ProjectsFolder, WorkspaceFailure>> ChooseAsync(string folder, CancellationToken cancellationToken)
    {
        Choices.Add(folder);
        Chosen = folder;

        return ValueTask.FromResult(Result<ProjectsFolder, WorkspaceFailure>.Success(new ProjectsFolder(Suggested, Chosen)));
    }
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
