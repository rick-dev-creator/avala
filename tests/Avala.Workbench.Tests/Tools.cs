using Avala.Jobs.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests;

internal static class Tools
{
    public static WorkedFiles Files() => new(new FakeCatalog(), new FakeChanges());

    public static ToolViewModel Of(ToolEntry entry) => Of(entry, new FakeClipboard());

    public static ToolViewModel Of(ToolEntry entry, FakeClipboard clipboard) => new(entry, clipboard, new FileViews(JobId.New(), Files()));

    public static ToolViewModel InWorktree(ToolEntry entry, FakeChanges changes)
    {
        var catalog = new FakeCatalog();
        var job = catalog.Add("Fix JPY rounding", JobStatus.Running).Summary.Job;
        catalog.Change(job, history => history with { Summary = history.Summary with { Workspace = WorkspaceId.New() } });

        return new(entry, new FakeClipboard(), new FileViews(job, new WorkedFiles(catalog, changes)));
    }

    public static FileDiff Diff(string path) =>
        new(path, false, [new DiffHunk(12, 2, 12, 2, "func ToMinor", [new DiffLine(DiffLineKind.Removed, "    return units * 100"), new DiffLine(DiffLineKind.Added, "    return units * pow10(exponent)")])]);
}
