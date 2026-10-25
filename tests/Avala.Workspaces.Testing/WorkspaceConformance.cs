using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

public sealed record WorkspaceServices(
    IWorkspaces Workspaces,
    IWorkspaceChanges Changes,
    IWorkspaceCheckpoints Checkpoints,
    IWorkspaceShelf Shelf,
    IBaseFiles BaseFiles,
    IWorkingFiles Files,
    IWorkingCommits Commits);

public static class WorkspaceConformance
{
    private const string Rules = ".avala/permissions.json";

    private const string Numbers = "src/numbers.txt";

    public static IReadOnlyList<(string Path, string Content)> Seed { get; } =
    [
        ("README.md", "# Shop\n"),
        (Rules, "{ \"rules\": [] }\n"),
        (Numbers, "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\nten\n"),
    ];

    public static async Task<IReadOnlyList<string>> CheckAsync(WorkspaceServices services, string repository, CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        if (!(await services.Workspaces.PrepareAsync(new WorkspaceRequest(repository), cancellationToken)).TryGetValue(out var prepared, out var failure))
        {
            return [$"preparing failed with {failure}"];
        }

        await CheckPreparedAsync(services, prepared, problems, cancellationToken);
        await EditAsync(prepared.Path, cancellationToken);
        await CheckEditedAsync(services, prepared, problems, cancellationToken);
        await CheckRestoredAsync(services, prepared, problems, cancellationToken);
        await CheckMergedAsync(services, prepared, repository, problems, cancellationToken);
        await CheckShelvedAsync(services, prepared, problems, cancellationToken);
        await CheckWorkingFilesAsync(services, repository, problems, cancellationToken);
        await CheckRemovedAsync(services, prepared, problems, cancellationToken);

        return problems;
    }

    private static async Task CheckPreparedAsync(WorkspaceServices services, WorkspaceInfo prepared, List<string> problems, CancellationToken cancellationToken)
    {
        Check(await ReadAsync(prepared.Path, "README.md", cancellationToken) == "# Shop\n", problems, "the worktree does not hold the repository's files");
        Check(prepared.BaseBranch == Option<string>.Some("main") && prepared.RulesCommit == prepared.BaseCommit, problems, $"the workspace names no base branch or another rules commit: {prepared}");
        Check((await services.Workspaces.FindAtAsync(prepared.Path, cancellationToken)).Match(found => found.Id == prepared.Id, _ => false), problems, "the worktree's folder does not find its workspace");
        Check((await services.Workspaces.FindAtAsync(Path.Combine(prepared.Path, "src"), cancellationToken)).Match(_ => false, error => error == WorkspaceFailure.UnknownWorkspace), problems, "a folder inside the worktree finds a workspace");

        var rules = await services.BaseFiles.ReadAsync(prepared.Path, Rules, cancellationToken);
        Check(rules.Match(file => file.Content == Option<string>.Some("{ \"rules\": [] }\n") && !file.Origin.EditedInWorktree && file.Origin.Commit == prepared.RulesCommit, _ => false), problems, $"the rule file reads otherwise: {Shown(rules)}");

        var absent = await services.BaseFiles.ReadAsync(prepared.Path, ".avala/budget.json", cancellationToken);
        Check(absent.Match(file => file.Content.IsNone && !file.Origin.EditedInWorktree, _ => false), problems, $"an absent rule file reads otherwise: {Shown(absent)}");
    }

    private static async Task EditAsync(string worktree, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(worktree, Numbers), "one\ntwo\nthree\nfour\nFIVE\nsix\nseven\neight\nnine\nten\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(worktree, "added.txt"), "new\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(worktree, Rules), "{ \"rules\": [\"allow\"] }\n", cancellationToken);
        File.Delete(Path.Combine(worktree, "README.md"));
    }

    private static async Task CheckEditedAsync(WorkspaceServices services, WorkspaceInfo prepared, List<string> problems, CancellationToken cancellationToken)
    {
        var rules = await services.BaseFiles.ReadAsync(prepared.Path, Rules, cancellationToken);
        Check(rules.Match(file => file.Content == Option<string>.Some("{ \"rules\": [] }\n") && file.Origin.EditedInWorktree, _ => false), problems, $"an edited rule file does not keep its committed content and say it was edited: {Shown(rules)}");

        var first = await services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        var again = await services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        var empty = await services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 2", cancellationToken);
        Check(Number(first) == 1 && Number(again) == 1 && Commit(first) == Commit(again), problems, "the same checkpoint taken again with nothing changed is not the same checkpoint");
        Check(Number(empty) == 2 && Commit(empty) != Commit(first), problems, "a checkpoint with a new label and nothing changed is not a new checkpoint");

        var expected = new[]
        {
            new FileChange(Rules, ChangeKind.Modified, 1, 1),
            new FileChange("README.md", ChangeKind.Deleted, 0, 1),
            new FileChange("added.txt", ChangeKind.Added, 1, 0),
            new FileChange(Numbers, ChangeKind.Modified, 1, 1),
        };
        var diff = await services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.SequenceEqual(expected) && found.BaseCommit == prepared.BaseCommit, _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files)))}");

        var file = await services.Changes.FileDiffAsync(prepared.Id, Numbers, cancellationToken);
        Check(file.Match(Hunk, _ => false), problems, $"the file's diff reads otherwise: {Shown(file.Map(found => string.Join(" | ", found.Hunks.Select(hunk => $"{hunk.OldStart},{hunk.OldLines} {hunk.NewStart},{hunk.NewLines} '{hunk.Section}' {string.Join("/", hunk.Lines)}"))))}");

        var unchanged = await services.Changes.FileDiffAsync(prepared.Id, "src/missing.txt", cancellationToken);
        Check(unchanged.Match(_ => false, error => error == WorkspaceFailure.FileUnchanged), problems, "a file the work did not touch has a diff");

        var listed = await services.Checkpoints.ListAsync(prepared.Id, cancellationToken);
        Check(listed.Match(found => found.Select(checkpoint => checkpoint.Files.Count).SequenceEqual([4, 0]), _ => false), problems, $"the checkpoints list otherwise: {Shown(listed.Map(found => string.Join(", ", found.Select(checkpoint => $"{checkpoint.Number} {checkpoint.Label} {checkpoint.Files.Count}"))))}");
    }

    private static async Task CheckRestoredAsync(WorkspaceServices services, WorkspaceInfo prepared, List<string> problems, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(prepared.Path, "added.txt"), "new\nlater\n", cancellationToken);
        var restored = await services.Checkpoints.RestoreAsync(prepared.Id, 1, cancellationToken);
        Check(restored.Match(found => found.Number == 4 && found.Files.SequenceEqual([new FileChange("added.txt", ChangeKind.Modified, 0, 1)]), _ => false), problems, $"restoring reads otherwise: {Shown(restored.Map(found => $"{found.Number} {string.Join(", ", found.Files)}"))}");
        Check(await ReadAsync(prepared.Path, "added.txt", cancellationToken) == "new\n", problems, "restoring did not bring the worktree back");
        Check((await services.Checkpoints.RestoreAsync(prepared.Id, 99, cancellationToken)).Match(_ => false, error => error == WorkspaceFailure.UnknownCheckpoint), problems, "restoring an unknown checkpoint is not refused");
    }

    private static async Task CheckMergedAsync(WorkspaceServices services, WorkspaceInfo prepared, string repository, List<string> problems, CancellationToken cancellationToken)
    {
        var conflicts = await services.Changes.ConflictsAsync(prepared.Id, cancellationToken);
        Check(conflicts.Match(found => found.Count == 0, _ => false), problems, $"the work conflicts: {Shown(conflicts.Map(found => string.Join(", ", found)))}");

        var merged = await services.Changes.MergeAsync(prepared.Id, "Merge the work", cancellationToken);
        Check(merged.Match(found => found.Commit.IsSome && found.BaseBranch == "main" && found.Checkout.Match(checkout => Same(checkout, repository), () => false), _ => false), problems, $"merging reads otherwise: {Shown(merged)}");
        Check(await ReadAsync(repository, "added.txt", cancellationToken) == "new\n" && !File.Exists(Path.Combine(repository, "README.md")), problems, "the merged work did not reach the repository's checkout");

        var again = await services.Changes.MergeAsync(prepared.Id, "Merge the work again", cancellationToken);
        Check(again.Match(found => found.Commit.IsNone, _ => false), problems, $"merging again with nothing new commits: {Shown(again)}");
    }

    private static async Task CheckShelvedAsync(WorkspaceServices services, WorkspaceInfo prepared, List<string> problems, CancellationToken cancellationToken)
    {
        Check((await services.Shelf.DeliveredAsync(prepared.Id, cancellationToken)).Match(delivered => delivered, _ => false), problems, "merged work is not delivered");

        await File.WriteAllTextAsync(Path.Combine(prepared.Path, "kept.txt"), "kept\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(prepared.Path, ".gitignore"), "local.env\nbin/\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(prepared.Path, "local.env"), "TOKEN=secret\n", cancellationToken);
        Directory.CreateDirectory(Path.Combine(prepared.Path, "bin"));
        await File.WriteAllTextAsync(Path.Combine(prepared.Path, "bin", "app.dll"), "built\n", cancellationToken);
        var shelved = await services.Shelf.ShelveAsync(prepared.Id, cancellationToken);
        Check(shelved.Match(found => found.Shelved, _ => false) && !Directory.Exists(prepared.Path), problems, $"shelving does not free the worktree: {Shown(shelved)}");
        Check((await services.Workspaces.FindAsync(prepared.Id, cancellationToken)).Match(found => found.Shelved, _ => false), problems, "a shelved workspace is not found as shelved");
        Check((await services.Workspaces.ReconcileAsync(cancellationToken)).Missing.All(missing => missing.Id != prepared.Id), problems, "a shelved workspace is reported missing");

        var rules = await services.BaseFiles.ReadAsync(prepared.Path, Rules, cancellationToken);
        Check(rules.Match(file => file.Content == Option<string>.Some("{ \"rules\": [] }\n") && file.Origin.EditedInWorktree, _ => false), problems, $"a shelved workspace's rule file does not keep its committed content and its edit: {Shown(rules)}");

        var diff = await services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.Any(file => file.Path == "kept.txt"), _ => false), problems, $"a shelved workspace's diff does not hold the changes it kept: {Shown(diff.Map(found => string.Join(", ", found.Files)))}");
        Check((await services.Shelf.DeliveredAsync(prepared.Id, cancellationToken)).Match(delivered => !delivered, _ => false), problems, "work kept after the merge is delivered");

        var rebuilt = await services.Shelf.RebuildAsync(prepared.Id, cancellationToken);
        Check(rebuilt.Match(found => !found.Shelved && found.Path == prepared.Path, _ => false), problems, $"rebuilding reads otherwise: {Shown(rebuilt)}");
        Check(await ReadAsync(prepared.Path, "kept.txt", cancellationToken) == "kept\n" && await ReadAsync(prepared.Path, "added.txt", cancellationToken) == "new\n", problems, "a rebuilt worktree does not hold its work");
        Check(await ReadAsync(prepared.Path, "local.env", cancellationToken) == "TOKEN=secret\n", problems, "a rebuilt worktree does not hold the ignored file it kept");
        Check(!File.Exists(Path.Combine(prepared.Path, "bin", "app.dll")), problems, "a rebuilt worktree brings back its build output");
        Check((await services.Workspaces.CheckpointAsync(prepared.Id, "After rebuilding", cancellationToken)).IsSuccess, problems, "a rebuilt workspace takes no checkpoint");
    }

    private static async Task CheckWorkingFilesAsync(WorkspaceServices services, string repository, List<string> problems, CancellationToken cancellationToken)
    {
        var written = await services.Files.WriteAsync(repository, ".avala/checks.json", "{ \"checks\": [] }\n", cancellationToken);
        var read = await services.Files.ReadAsync(repository, ".avala/checks.json", cancellationToken);
        Check(written.IsSuccess && read.Match(found => found == Option<string>.Some("{ \"checks\": [] }\n"), _ => false), problems, "a working file written is not read back");
        Check((await services.Files.ReadAsync(repository, "../outside.txt", cancellationToken)).Match(_ => false, error => error == WorkspaceFailure.OutsideRepository), problems, "a path outside the repository is not refused");

        var committed = await services.Commits.CommitAsync(repository, ".avala/checks.json", "Declare the checks", cancellationToken);
        Check(committed.Match(found => found.Branch == "main" && found.Commit.Length == 40, _ => false), problems, $"committing a working file reads otherwise: {Shown(committed)}");

        var again = await services.Commits.CommitAsync(repository, ".avala/checks.json", "Declare the checks again", cancellationToken);
        Check(again.Match(_ => false, error => error == WorkspaceFailure.FileUnchanged), problems, $"committing an unchanged file is not refused: {Shown(again)}");

        var current = await services.BaseFiles.ReadCurrentAsync(repository, ".avala/checks.json", cancellationToken);
        Check(current.Match(file => file.Content == Option<string>.Some("{ \"checks\": [] }\n") && !file.Origin.EditedInWorktree, _ => false), problems, $"the repository's current file reads otherwise: {Shown(current)}");
    }

    private static async Task CheckRemovedAsync(WorkspaceServices services, WorkspaceInfo prepared, List<string> problems, CancellationToken cancellationToken)
    {
        Check((await services.Workspaces.RemoveAsync(prepared.Id, cancellationToken)).IsSuccess, problems, "removing failed");
        Check((await services.Workspaces.FindAsync(prepared.Id, cancellationToken)).Match(_ => false, error => error == WorkspaceFailure.UnknownWorkspace), problems, "a removed workspace is still found");
        Check(!Directory.Exists(prepared.Path), problems, "a removed workspace leaves its worktree");
    }

    private static bool Hunk(FileDiff diff) =>
        diff is { Binary: false, Hunks: [{ OldStart: 2, OldLines: 7, NewStart: 2, NewLines: 7, Section: "one" } hunk] }
        && hunk.Lines.SequenceEqual(
        [
            new DiffLine(DiffLineKind.Context, "two"),
            new DiffLine(DiffLineKind.Context, "three"),
            new DiffLine(DiffLineKind.Context, "four"),
            new DiffLine(DiffLineKind.Removed, "five"),
            new DiffLine(DiffLineKind.Added, "FIVE"),
            new DiffLine(DiffLineKind.Context, "six"),
            new DiffLine(DiffLineKind.Context, "seven"),
            new DiffLine(DiffLineKind.Context, "eight"),
        ]);

    private static int Number(Result<CheckpointInfo, WorkspaceFailure> checkpoint) => checkpoint.Match(found => found.Number, _ => -1);

    private static string Commit(Result<CheckpointInfo, WorkspaceFailure> checkpoint) => checkpoint.Match(found => found.Commit, error => error.ToString());

    private static bool Same(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));

    private static async Task<string> ReadAsync(string folder, string path, CancellationToken cancellationToken) =>
        File.Exists(Path.Combine(folder, path)) ? await File.ReadAllTextAsync(Path.Combine(folder, path), cancellationToken) : string.Empty;

    private static string Shown<T>(Result<T, WorkspaceFailure> result) => result.Match(found => found?.ToString() ?? string.Empty, error => error.ToString());

    private static void Check(bool holds, List<string> problems, string problem)
    {
        if (!holds)
        {
            problems.Add(problem);
        }
    }
}
