using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

public static class CheckoutFidelity
{
    public static IReadOnlyDictionary<string, Func<FidelityBench, CancellationToken, Task<IReadOnlyList<string>>>> Cases { get; } =
        new Dictionary<string, Func<FidelityBench, CancellationToken, Task<IReadOnlyList<string>>>>(StringComparer.Ordinal)
        {
            [nameof(RefusesToMergeAFileOverAnUntrackedFolderOfTheBaseCheckoutAsync)] = RefusesToMergeAFileOverAnUntrackedFolderOfTheBaseCheckoutAsync,
            [nameof(RefusesToMergeAFolderOverAnUntrackedFileOfTheBaseCheckoutAsync)] = RefusesToMergeAFolderOverAnUntrackedFileOfTheBaseCheckoutAsync,
            [nameof(RefusesToTurnATrackedFolderHoldingAnUntrackedFileIntoAFileAsync)] = RefusesToTurnATrackedFolderHoldingAnUntrackedFileIntoAFileAsync,
            [nameof(RestoresAFileWhereTheFolderHoldsIgnoredBuildOutputAsync)] = RestoresAFileWhereTheFolderHoldsIgnoredBuildOutputAsync,
            [nameof(CheckpointsALinkToItsOwnParentFolderAsALinkAsync)] = CheckpointsALinkToItsOwnParentFolderAsALinkAsync,
            [nameof(ClonesFromAWorktreesPathAsync)] = ClonesFromAWorktreesPathAsync,
            [nameof(CommitsNothingForAFileTheRepositoryIgnoresAsync)] = CommitsNothingForAFileTheRepositoryIgnoresAsync,
            [nameof(RefusesToPublishToAnAddressThatNamesNothingAsync)] = RefusesToPublishToAnAddressThatNamesNothingAsync,
            [nameof(KeepsTheWorksExecutableBitWhenTheBaseChangedAnotherLineOfTheFileAsync)] = KeepsTheWorksExecutableBitWhenTheBaseChangedAnotherLineOfTheFileAsync,
            [nameof(RefusesToCloneAnAddressThatIsNeitherAUrlNorARepositoryAsync)] = RefusesToCloneAnAddressThatIsNeitherAUrlNorARepositoryAsync,
        };

    private static async Task<IReadOnlyList<string>> RefusesToMergeAFileOverAnUntrackedFolderOfTheBaseCheckoutAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);
        await WorkspaceFidelity.WriteAsync(bench.Repository, "notes/today.md", "a draft of the developer\n", cancellationToken);
        await WorkspaceFidelity.WriteAsync(prepared.Path, "notes", "the work's notes\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        return await RefusedAsync(bench, prepared, "notes/today.md", cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> RefusesToMergeAFolderOverAnUntrackedFileOfTheBaseCheckoutAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);
        await WorkspaceFidelity.WriteAsync(bench.Repository, "notes", "a draft of the developer\n", cancellationToken);
        await WorkspaceFidelity.WriteAsync(prepared.Path, "notes/today.md", "the work's notes\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        return await RefusedAsync(bench, prepared, "notes", cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> RefusesToTurnATrackedFolderHoldingAnUntrackedFileIntoAFileAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        await bench.CommitToBase("old/a.txt", "a\n", cancellationToken);
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);
        await WorkspaceFidelity.WriteAsync(bench.Repository, "old/draft.txt", "a draft of the developer\n", cancellationToken);
        Directory.Delete(Path.Combine(prepared.Path, "old"), recursive: true);
        await WorkspaceFidelity.WriteAsync(prepared.Path, "old", "now a file\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        return await RefusedAsync(bench, prepared, "old/draft.txt", cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> RestoresAFileWhereTheFolderHoldsIgnoredBuildOutputAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", WorkspaceFidelity.Ignored, cancellationToken);
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);
        await WorkspaceFidelity.WriteAsync(prepared.Path, "tool", "a script\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        File.Delete(Path.Combine(prepared.Path, "tool"));
        await WorkspaceFidelity.WriteAsync(prepared.Path, "tool/Program.cs", "class Program;\n", cancellationToken);
        await WorkspaceFidelity.WriteAsync(prepared.Path, "tool/bin/tool.dll", "built\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 2", cancellationToken);

        Result<CheckpointChanges, WorkspaceFailure> restored;

        try
        {
            restored = await bench.Services.Checkpoints.RestoreAsync(prepared.Id, 1, cancellationToken);
        }
        catch (IOException exception)
        {
            problems.Add($"restoring threw {exception.GetType().Name}: {exception.Message}");

            return problems;
        }
        catch (UnauthorizedAccessException exception)
        {
            problems.Add($"restoring threw {exception.GetType().Name}: {exception.Message}");

            return problems;
        }

        WorkspaceFidelity.Check(restored.IsSuccess, problems, $"restoring reads otherwise: {WorkspaceFidelity.Shown(restored)}");
        WorkspaceFidelity.Check(await WorkspaceFidelity.ReadAsync(prepared.Path, "tool", cancellationToken) == "a script\n", problems, "restoring did not bring the file back where the folder was");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> CheckpointsALinkToItsOwnParentFolderAsALinkAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        var problems = new List<string>();
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);
        Directory.CreateDirectory(Path.Combine(prepared.Path, "src"));
        _ = Directory.CreateSymbolicLink(Path.Combine(prepared.Path, "src", "loop"), "..");

        try
        {
            _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        }
        catch (IOException exception)
        {
            problems.Add($"checkpointing threw {exception.GetType().Name}: {exception.Message}");

            return problems;
        }

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        WorkspaceFidelity.Check(diff.Match(found => found.Files.Select(file => file.Path).SequenceEqual(["src/loop"]), _ => false), problems, $"the diff lists otherwise: {WorkspaceFidelity.Shown(diff.Map(found => string.Join(", ", found.Files.Select(file => file.Path))))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ClonesFromAWorktreesPathAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);
        var parent = Path.GetDirectoryName(bench.Repository)!;

        var cloned = await bench.Remotes.CloneAsync(prepared.Path, parent, "clone", cancellationToken);
        WorkspaceFidelity.Check(cloned.IsSuccess, problems, $"cloning a worktree's path reads otherwise: {WorkspaceFidelity.Shown(cloned)}");
        WorkspaceFidelity.Check(await WorkspaceFidelity.ReadAsync(Path.Combine(parent, "clone"), "README.md", cancellationToken) == "# Shop\n", problems, "the clone does not hold the worktree's files");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> CommitsNothingForAFileTheRepositoryIgnoresAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", ".avala/local/\n", cancellationToken);
        await WorkspaceFidelity.WriteAsync(bench.Repository, ".avala/local/notes.md", "kept out\n", cancellationToken);

        var committed = await bench.Services.Commits.CommitAsync(bench.Repository, ".avala/local/notes.md", "Commit the notes", cancellationToken);
        WorkspaceFidelity.Check(committed.Match(_ => false, error => error == WorkspaceFailure.FileUnchanged), problems, $"committing an ignored file reads otherwise: {WorkspaceFidelity.Shown(committed)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToPublishToAnAddressThatNamesNothingAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        var published = await bench.Remotes.PublishAsync(bench.Repository, "origin", "no-such-remote", cancellationToken);
        WorkspaceFidelity.Check(!published.IsSuccess, problems, $"publishing to an address that names nothing reads otherwise: {WorkspaceFidelity.Shown(published)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> KeepsTheWorksExecutableBitWhenTheBaseChangedAnotherLineOfTheFileAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        var problems = new List<string>();
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);
        await bench.CommitToBase(WorkspaceFidelity.Numbers, WorkspaceFidelity.Tens.Replace("one\n", "ONE\n", StringComparison.Ordinal), cancellationToken);
        var worked = Path.Combine(prepared.Path, WorkspaceFidelity.Numbers);
        await WorkspaceFidelity.WriteAsync(prepared.Path, WorkspaceFidelity.Numbers, WorkspaceFidelity.Tens.Replace("ten\n", "TEN\n", StringComparison.Ordinal), cancellationToken);
        File.SetUnixFileMode(worked, File.GetUnixFileMode(worked) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        WorkspaceFidelity.Check(merged.Match(found => found.Commit.IsSome, _ => false), problems, $"merging reads otherwise: {WorkspaceFidelity.Shown(merged)}");
        WorkspaceFidelity.Check(File.GetUnixFileMode(Path.Combine(bench.Repository, WorkspaceFidelity.Numbers)).HasFlag(UnixFileMode.UserExecute), problems, "the merge dropped the executable bit the work gave the file");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToCloneAnAddressThatIsNeitherAUrlNorARepositoryAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;

        var cloned = await bench.Remotes.CloneAsync("no-such-shop", parent, "clone", cancellationToken);
        WorkspaceFidelity.Check(!cloned.IsSuccess, problems, $"cloning an address that names nothing reads otherwise: {WorkspaceFidelity.Shown(cloned)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusedAsync(FidelityBench bench, WorkspaceInfo prepared, string kept, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var head = await bench.Index.CommitsAsync(bench.Repository, 1, cancellationToken);
        Result<MergedWork, WorkspaceFailure> merged;

        try
        {
            merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        }
        catch (IOException exception)
        {
            merged = WorkspaceFailure.GitFailed;
            problems.Add($"merging threw {exception.GetType().Name}: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            merged = WorkspaceFailure.GitFailed;
            problems.Add($"merging threw {exception.GetType().Name}: {exception.Message}");
        }

        WorkspaceFidelity.Check(merged.Match(_ => false, error => error == WorkspaceFailure.BaseCheckoutDirty), problems, $"merging over the developer's untracked {kept} reads otherwise: {WorkspaceFidelity.Shown(merged)}");
        WorkspaceFidelity.Check(await WorkspaceFidelity.ReadAsync(bench.Repository, kept, cancellationToken) == "a draft of the developer\n", problems, $"the merge did not leave the developer's {kept} as it was");
        var after = await bench.Index.CommitsAsync(bench.Repository, 1, cancellationToken);
        WorkspaceFidelity.Check(after.SequenceEqual(head), problems, $"the refused merge still moved the base branch to {string.Join(" | ", after.Select(commit => commit.Subject))}");

        return problems;
    }
}
