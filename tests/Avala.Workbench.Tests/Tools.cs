using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Linking;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests;

internal static class Tools
{
    public static WorkedFiles Files() => new(new FakeCatalog(), new FakeChanges(), new FakeWorkspaces(), new FakeWorkingFiles());

    public static FileViewing Viewing() => Viewing(Files());

    public static FileViewing Viewing(WorkedFiles worked) => new(worked, FakeLinks.Opening, new FakePictures(), Replies(FakeLinks.Opening));

    public static ReplyTools Replies(Links links, CodeFiles? code = null) => new(code ?? Code, new JobReferences(new FakeCatalog(), new Avala.Workbench.Tests.RepositoryChoice.FakeRemotes(), links));

    public static FileViews Reply(FakeLinks? opener = null, CodeFiles? code = null, IMessenger? messenger = null)
    {
        var links = new Links(opener ?? new FakeLinks(), messenger ?? new StrongReferenceMessenger());

        return new FileViewing(Files(), links, new FakePictures(), Replies(links, code)).For(JobId.New());
    }

    public static Avala.Workbench.Linking.CodeFiles Code { get; } = new(new FakeFileSaver(), new FakeOpener(), new Avala.Sdk.AvalaPaths("/home/ana/.local/share/Avala"));

    public static ToolViewModel Of(ToolEntry entry) => Of(entry, new FakeClipboard());

    public static ToolViewModel Of(ToolEntry entry, FakeClipboard clipboard) => new(entry, clipboard, Viewing().For(JobId.New()));

    public static ToolViewModel InWorktree(ToolEntry entry, FakeChanges changes) => new Worktree { Changes = changes }.Tool(entry);

    public static FileDiff Diff(string path) =>
        new(path, false, [new DiffHunk(12, 2, 12, 2, "func ToMinor", [new DiffLine(DiffLineKind.Removed, "    return units * 100"), new DiffLine(DiffLineKind.Added, "    return units * pow10(exponent)")])]);
}

internal sealed class Worktree
{
    public static readonly string Folder = HostPaths.Rooted("/worktrees/fix-rounding");

    public static readonly string Repository = HostPaths.Rooted("/repo");

    public FakeChanges Changes { get; init; } = new();

    public FakeWorkingFiles Files { get; } = new();

    public FakeLinks Opener { get; } = new();

    public FakePictures Pictures { get; } = new();

    public IMessenger Messenger { get; } = new StrongReferenceMessenger();

    public bool Gone { get; init; }

    public bool Freed { get; init; }

    public Avala.Workbench.Tests.RepositoryChoice.FakeRemotes Remotes { get; } = new();

    private FakeCatalog catalog = new();

    public JobId Job { get; private set; }

    public WorkedFiles Worked()
    {
        catalog = new FakeCatalog();
        var workspaces = new FakeWorkspaces();
        var workspace = WorkspaceId.New();
        Job = catalog.Add("Draw the solar system", JobStatus.Running).Summary.Job;
        catalog.Change(Job, history => history with { Summary = history.Summary with { Workspace = workspace, Repository = Repository } });

        if (!Gone)
        {
            workspaces.Known[workspace] = new WorkspaceInfo(workspace, Folder, "avala/solar", "ba5e") { Shelved = Freed };
        }

        return new WorkedFiles(catalog, Changes, workspaces, Files);
    }

    public ToolViewModel Tool(ToolEntry entry)
    {
        var worked = Worked();

        return new(entry, new FakeClipboard(), Viewing(worked).For(Job));
    }

    public FileViewing Viewing(WorkedFiles worked)
    {
        var links = new Links(Opener, Messenger);

        return new(worked, links, Pictures, new ReplyTools(Tools.Code, new JobReferences(catalog, Remotes, links)));
    }

    public MessageViewModel Message(string text)
    {
        var worked = Worked();

        return new(new MessageEntry("m", text, Avala.Agents.Contracts.Events.ItemOutcome.Succeeded), new FakeClipboard(), Viewing(worked).For(Job));
    }
}
