using System.Text;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

public static class RepositoryFidelity
{
    public static IReadOnlyDictionary<string, Func<FidelityBench, CancellationToken, Task<IReadOnlyList<string>>>> Cases { get; } =
        new Dictionary<string, Func<FidelityBench, CancellationToken, Task<IReadOnlyList<string>>>>(StringComparer.Ordinal)
        {
            [nameof(NamesAMergeCommitByTheFirstLineOfItsMessageAsync)] = NamesAMergeCommitByTheFirstLineOfItsMessageAsync,
            [nameof(ListsNoIgnoredFileAmongTheRepositorysFilesAsync)] = ListsNoIgnoredFileAmongTheRepositorysFilesAsync,
            [nameof(ListsAFileWithAnAccentedNameByItsNameAsync)] = ListsAFileWithAnAccentedNameByItsNameAsync,
            [nameof(ClonesWhatTheRemoteHoldsAsync)] = ClonesWhatTheRemoteHoldsAsync,
            [nameof(RefusesToCloneARemoteThatDoesNotExistAsync)] = RefusesToCloneARemoteThatDoesNotExistAsync,
            [nameof(RefusesToPublishToARemoteThatDoesNotExistAsync)] = RefusesToPublishToARemoteThatDoesNotExistAsync,
            [nameof(ListsUntrackedFilesBeforeTrackedOnesAsync)] = ListsUntrackedFilesBeforeTrackedOnesAsync,
            [nameof(ListsATrackedFileDeletedFromTheCheckoutAsync)] = ListsATrackedFileDeletedFromTheCheckoutAsync,
            [nameof(RefusesToCloneAFolderThatIsNotARepositoryAsync)] = RefusesToCloneAFolderThatIsNotARepositoryAsync,
            [nameof(ClonesTheRemotesHistoryAsync)] = ClonesTheRemotesHistoryAsync,
            [nameof(ClonesAnExecutableFileAsExecutableAsync)] = ClonesAnExecutableFileAsExecutableAsync,
            [nameof(InitializingAFolderLeavesItsFilesAsTheyWereAsync)] = InitializingAFolderLeavesItsFilesAsTheyWereAsync,
            [nameof(RefusesToPublishToAFolderThatIsNotARepositoryAsync)] = RefusesToPublishToAFolderThatIsNotARepositoryAsync,
            [nameof(ListsTheFilesOfASubfolderWithoutWhatTheRootIgnoresAsync)] = ListsTheFilesOfASubfolderWithoutWhatTheRootIgnoresAsync,
        };

    private static async Task<IReadOnlyList<string>> NamesAMergeCommitByTheFirstLineOfItsMessageAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);
        await WorkspaceFidelity.WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        _ = await bench.Services.Changes.MergeAsync(prepared.Id, "Add the file\n\nSquashed by Avala from the checkpoints of 1 attempt.\n\nAvala-Job: 1", cancellationToken);

        var commits = await bench.Index.CommitsAsync(bench.Repository, 1, cancellationToken);
        WorkspaceFidelity.Check(commits is [{ Subject: "Add the file" }], problems, $"the latest commit reads otherwise: {string.Join(" | ", commits.Select(commit => commit.Subject))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsNoIgnoredFileAmongTheRepositorysFilesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", WorkspaceFidelity.Ignored, cancellationToken);
        await WorkspaceFidelity.WriteAsync(bench.Repository, "bin/Debug/Shop.dll", "built\n", cancellationToken);
        await WorkspaceFidelity.WriteAsync(bench.Repository, "notes.txt", "untracked\n", cancellationToken);

        var files = await bench.Index.FilesAsync(bench.Repository, 100, cancellationToken);
        WorkspaceFidelity.Check(files.Order(StringComparer.Ordinal).SequenceEqual([".avala/permissions.json", ".gitignore", "README.md", "notes.txt", WorkspaceFidelity.Numbers]), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsAFileWithAnAccentedNameByItsNameAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await WorkspaceFidelity.WriteAsync(bench.Repository, "docs/canción.md", "la la\n", cancellationToken);

        var files = await bench.Index.FilesAsync(bench.Repository, 100, cancellationToken);
        WorkspaceFidelity.Check(files.Contains("docs/canción.md"), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ClonesWhatTheRemoteHoldsAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;

        var cloned = await bench.Remotes.CloneAsync(bench.Repository, parent, "clone", cancellationToken);
        WorkspaceFidelity.Check(cloned.IsSuccess, problems, $"cloning reads otherwise: {WorkspaceFidelity.Shown(cloned)}");
        WorkspaceFidelity.Check(await WorkspaceFidelity.ReadAsync(Path.Combine(parent, "clone"), "README.md", cancellationToken) == "# Shop\n", problems, "the clone does not hold the remote's files");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToCloneARemoteThatDoesNotExistAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;

        var cloned = await bench.Remotes.CloneAsync(Path.Combine(parent, "missing.git"), parent, "clone", cancellationToken);
        WorkspaceFidelity.Check(cloned.IsFailure && !Directory.Exists(Path.Combine(parent, "clone")), problems, $"cloning a missing remote reads otherwise: {WorkspaceFidelity.Shown(cloned)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToPublishToARemoteThatDoesNotExistAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var missing = Path.Combine(Path.GetDirectoryName(bench.Repository)!, "missing.git");

        var published = await bench.Remotes.PublishAsync(bench.Repository, "origin", missing, cancellationToken);
        WorkspaceFidelity.Check(published.IsFailure, problems, $"publishing to a missing remote reads otherwise: {WorkspaceFidelity.Shown(published)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsUntrackedFilesBeforeTrackedOnesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await WorkspaceFidelity.WriteAsync(bench.Repository, "notes.txt", "untracked\n", cancellationToken);

        var files = await bench.Index.FilesAsync(bench.Repository, 100, cancellationToken);
        WorkspaceFidelity.Check(files.SequenceEqual(["notes.txt", ".avala/permissions.json", "README.md", WorkspaceFidelity.Numbers]), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsATrackedFileDeletedFromTheCheckoutAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        File.Delete(Path.Combine(bench.Repository, "README.md"));

        var files = await bench.Index.FilesAsync(bench.Repository, 100, cancellationToken);
        WorkspaceFidelity.Check(files.Contains("README.md"), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToCloneAFolderThatIsNotARepositoryAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;
        var plain = Path.Combine(parent, "plain");
        await WorkspaceFidelity.WriteAsync(plain, "notes.txt", "not a repository\n", cancellationToken);

        var cloned = await bench.Remotes.CloneAsync(plain, parent, "clone", cancellationToken);
        WorkspaceFidelity.Check(cloned.IsFailure && !Directory.Exists(Path.Combine(parent, "clone")), problems, $"cloning a folder that is not a repository reads otherwise: {WorkspaceFidelity.Shown(cloned)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ClonesTheRemotesHistoryAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;

        _ = await bench.Remotes.CloneAsync(bench.Repository, parent, "clone", cancellationToken);
        var original = await bench.Index.CommitsAsync(bench.Repository, 10, cancellationToken);
        var cloned = await bench.Index.CommitsAsync(Path.Combine(parent, "clone"), 10, cancellationToken);
        WorkspaceFidelity.Check(cloned.Select(commit => commit.Subject).SequenceEqual(original.Select(commit => commit.Subject)), problems, $"the clone's history reads {string.Join(" | ", cloned.Select(commit => commit.Subject))} where the remote's reads {string.Join(" | ", original.Select(commit => commit.Subject))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ClonesAnExecutableFileAsExecutableAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;
        var based = Path.Combine(bench.Repository, WorkspaceFidelity.Numbers);
        File.SetUnixFileMode(based, File.GetUnixFileMode(based) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        await bench.CommitToBase(WorkspaceFidelity.Numbers, WorkspaceFidelity.Tens, cancellationToken);

        _ = await bench.Remotes.CloneAsync(bench.Repository, parent, "clone", cancellationToken);
        WorkspaceFidelity.Check(File.GetUnixFileMode(Path.Combine(parent, "clone", WorkspaceFidelity.Numbers)).HasFlag(UnixFileMode.UserExecute), problems, "the clone does not keep the remote's executable bit");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> InitializingAFolderLeavesItsFilesAsTheyWereAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var plain = Path.Combine(Path.GetDirectoryName(bench.Repository)!, "plain");
        byte[] picture = [0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE, 0x00, 0xC3, 0x28];
        Directory.CreateDirectory(plain);
        await File.WriteAllBytesAsync(Path.Combine(plain, "logo.png"), picture, cancellationToken);

        var initialized = await bench.Repositories.InitializeAsync(plain, cancellationToken);
        WorkspaceFidelity.Check(initialized.IsSuccess, problems, $"initializing reads otherwise: {WorkspaceFidelity.Shown(initialized)}");
        WorkspaceFidelity.Check((await File.ReadAllBytesAsync(Path.Combine(plain, "logo.png"), cancellationToken)).AsSpan().SequenceEqual(picture), problems, "initializing the folder rewrote its binary file");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToPublishToAFolderThatIsNotARepositoryAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var plain = Path.Combine(Path.GetDirectoryName(bench.Repository)!, "plain");
        await WorkspaceFidelity.WriteAsync(plain, "notes.txt", "not a repository\n", cancellationToken);

        var published = await bench.Remotes.PublishAsync(bench.Repository, "origin", plain, cancellationToken);
        WorkspaceFidelity.Check(published.IsFailure, problems, $"publishing to a folder that is not a repository reads otherwise: {WorkspaceFidelity.Shown(published)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsTheFilesOfASubfolderWithoutWhatTheRootIgnoresAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", "bin/\n", cancellationToken);
        await WorkspaceFidelity.WriteAsync(bench.Repository, "src/bin/x.dll", "built\n", cancellationToken);

        var files = await bench.Index.FilesAsync(Path.Combine(bench.Repository, "src"), 100, cancellationToken);
        WorkspaceFidelity.Check(files.SequenceEqual(["numbers.txt"]), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }
}
