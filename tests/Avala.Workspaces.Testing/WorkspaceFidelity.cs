using System.Text;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

public static class WorkspaceFidelity
{
    private const string Numbers = "src/numbers.txt";

    private const string Ignored = "bin/\nobj/\n";

    private const string Tens = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\nten\n";

    public static IReadOnlyDictionary<string, Func<FidelityBench, CancellationToken, Task<IReadOnlyList<string>>>> Cases { get; } =
        new Dictionary<string, Func<FidelityBench, CancellationToken, Task<IReadOnlyList<string>>>>(StringComparer.Ordinal)
        {
            [nameof(MergesAChangeOnAnotherLineOfAFileTheBaseAlsoChangedAsync)] = MergesAChangeOnAnotherLineOfAFileTheBaseAlsoChangedAsync,
            [nameof(ReportsAChangeOnTheSameLineTheBaseChangedAsAConflictAsync)] = ReportsAChangeOnTheSameLineTheBaseChangedAsAConflictAsync,
            [nameof(MergesIntoABaseCheckoutHoldingAnUntrackedFileAndKeepsItAsync)] = MergesIntoABaseCheckoutHoldingAnUntrackedFileAndKeepsItAsync,
            [nameof(MergesIntoABaseCheckoutHoldingIgnoredBuildOutputAsync)] = MergesIntoABaseCheckoutHoldingIgnoredBuildOutputAsync,
            [nameof(LeavesIgnoredBuildOutputOfTheWorkOutOfItsDiffAndItsMergeAsync)] = LeavesIgnoredBuildOutputOfTheWorkOutOfItsDiffAndItsMergeAsync,
            [nameof(RestoringACheckpointKeepsIgnoredBuildOutputAsync)] = RestoringACheckpointKeepsIgnoredBuildOutputAsync,
            [nameof(CountsAndShowsTheLossOfAFinalNewlineAsync)] = CountsAndShowsTheLossOfAFinalNewlineAsync,
            [nameof(CutsAHunksSectionHeadingAtEightyBytesAsync)] = CutsAHunksSectionHeadingAtEightyBytesAsync,
            [nameof(TakesOnlyALineStartingWithAnAsciiLetterAsASectionHeadingAsync)] = TakesOnlyALineStartingWithAnAsciiLetterAsASectionHeadingAsync,
            [nameof(PlacesAndCountsAnAmbiguousChangeWhereGitDoesAsync)] = PlacesAndCountsAnAmbiguousChangeWhereGitDoesAsync,
            [nameof(SeesAFileTheWorkMadeExecutableAsync)] = SeesAFileTheWorkMadeExecutableAsync,
            [nameof(SeesASymbolicLinkTheWorkAddedAsync)] = SeesASymbolicLinkTheWorkAddedAsync,
            [nameof(ListsChangedFilesInGitsByteOrderAsync)] = ListsChangedFilesInGitsByteOrderAsync,
            [nameof(MergesAChildIntoItsParentsWorktreeHoldingAnUntrackedFileAsync)] = MergesAChildIntoItsParentsWorktreeHoldingAnUntrackedFileAsync,
            [nameof(NamesAMergeCommitByTheFirstLineOfItsMessageAsync)] = NamesAMergeCommitByTheFirstLineOfItsMessageAsync,
            [nameof(ListsNoIgnoredFileAmongTheRepositorysFilesAsync)] = ListsNoIgnoredFileAmongTheRepositorysFilesAsync,
            [nameof(ListsAFileWithAnAccentedNameByItsNameAsync)] = ListsAFileWithAnAccentedNameByItsNameAsync,
            [nameof(ClonesWhatTheRemoteHoldsAsync)] = ClonesWhatTheRemoteHoldsAsync,
            [nameof(RefusesToCloneARemoteThatDoesNotExistAsync)] = RefusesToCloneARemoteThatDoesNotExistAsync,
            [nameof(RefusesToPublishToARemoteThatDoesNotExistAsync)] = RefusesToPublishToARemoteThatDoesNotExistAsync,
            [nameof(DiffsCrlfEmptyRevertedAndSpacedFilesAsGitDoesAsync)] = DiffsCrlfEmptyRevertedAndSpacedFilesAsGitDoesAsync,
            [nameof(MergesTwiceWithOnlyTheNewWorkTheSecondTimeAsync)] = MergesTwiceWithOnlyTheNewWorkTheSecondTimeAsync,
            [nameof(ReadsTheCurrentFileFromASubfolderAndCommitsInsideAWorktreeAsync)] = ReadsTheCurrentFileFromASubfolderAndCommitsInsideAWorktreeAsync,
            [nameof(PreparesOnAWorktreeWithItsBranchAsTheBaseAndMergesBackIntoItAsync)] = PreparesOnAWorktreeWithItsBranchAsTheBaseAndMergesBackIntoItAsync,
            [nameof(KeepsATrackedFileThatAnIgnoreRuleMatchesAsync)] = KeepsATrackedFileThatAnIgnoreRuleMatchesAsync,
            [nameof(IgnoresWhatACharacterClassNamesAsync)] = IgnoresWhatACharacterClassNamesAsync,
            [nameof(IgnoresAPatternWithAnEscapedHashAsync)] = IgnoresAPatternWithAnEscapedHashAsync,
            [nameof(LeavesAFolderNamedDotGitOutOfTheWorkAsync)] = LeavesAFolderNamedDotGitOutOfTheWorkAsync,
            [nameof(ListsUntrackedFilesBeforeTrackedOnesAsync)] = ListsUntrackedFilesBeforeTrackedOnesAsync,
            [nameof(ListsATrackedFileDeletedFromTheCheckoutAsync)] = ListsATrackedFileDeletedFromTheCheckoutAsync,
            [nameof(KeepsTheBasesExecutableBitWhenMergingAChangeToTheFileAsync)] = KeepsTheBasesExecutableBitWhenMergingAChangeToTheFileAsync,
            [nameof(RefusesToMergeOverAnUntrackedFileOfTheBaseCheckoutAsync)] = RefusesToMergeOverAnUntrackedFileOfTheBaseCheckoutAsync,
            [nameof(MergesAChangeIntoAFileTheBaseRenamedAsync)] = MergesAChangeIntoAFileTheBaseRenamedAsync,
            [nameof(RefusesToCloneAFolderThatIsNotARepositoryAsync)] = RefusesToCloneAFolderThatIsNotARepositoryAsync,
            [nameof(ClonesTheRemotesHistoryAsync)] = ClonesTheRemotesHistoryAsync,
            [nameof(ClonesAnExecutableFileAsExecutableAsync)] = ClonesAnExecutableFileAsExecutableAsync,
            [nameof(InitializingAFolderLeavesItsFilesAsTheyWereAsync)] = InitializingAFolderLeavesItsFilesAsTheyWereAsync,
            [nameof(RestoresACheckpointWhereAFolderWasStillAFileAsync)] = RestoresACheckpointWhereAFolderWasStillAFileAsync,
            [nameof(RestoringACheckpointRemovesTheFolderItEmptiedAsync)] = RestoringACheckpointRemovesTheFolderItEmptiedAsync,
            [nameof(RefusesToPublishToAFolderThatIsNotARepositoryAsync)] = RefusesToPublishToAFolderThatIsNotARepositoryAsync,
        };

    public static IReadOnlyDictionary<string, string> Approximated { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(PlacesAndCountsAnAmbiguousChangeWhereGitDoesAsync)] =
            "The in-memory diff is plain Myers; git's xdiff also discards repeated lines, cuts long searches and slides change groups by indentation, so an ambiguous change may be placed and counted otherwise. Scenarios do not depend on it; see the plan's fast tests entry.",
    };

    private static async Task<IReadOnlyList<string>> MergesAChangeOnAnotherLineOfAFileTheBaseAlsoChangedAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await bench.CommitToBase(Numbers, Tens.Replace("one\n", "ONE\n", StringComparison.Ordinal), cancellationToken);
        await WriteAsync(prepared.Path, Numbers, Tens.Replace("ten\n", "TEN\n", StringComparison.Ordinal), cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var conflicts = await bench.Services.Changes.ConflictsAsync(prepared.Id, cancellationToken);
        Check(conflicts.Match(found => found.Count == 0, _ => false), problems, $"a change on line 10 conflicts with the base's change on line 1: {Shown(conflicts.Map(found => string.Join(", ", found)))}");

        var merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        Check(merged.Match(found => found.Commit.IsSome, _ => false), problems, $"merging reads otherwise: {Shown(merged)}");
        Check(await ReadAsync(bench.Repository, Numbers, cancellationToken) == Tens.Replace("one\n", "ONE\n", StringComparison.Ordinal).Replace("ten\n", "TEN\n", StringComparison.Ordinal), problems, "the base checkout does not hold both changes");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ReportsAChangeOnTheSameLineTheBaseChangedAsAConflictAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await bench.CommitToBase(Numbers, Tens.Replace("five\n", "Five\n", StringComparison.Ordinal), cancellationToken);
        await WriteAsync(prepared.Path, Numbers, Tens.Replace("five\n", "FIVE\n", StringComparison.Ordinal), cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var conflicts = await bench.Services.Changes.ConflictsAsync(prepared.Id, cancellationToken);
        Check(conflicts.Match(found => found.SequenceEqual([Numbers]), _ => false), problems, $"the conflicts read otherwise: {Shown(conflicts.Map(found => string.Join(", ", found)))}");
        var merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        Check(merged.Match(_ => false, error => error == WorkspaceFailure.MergeConflict), problems, $"merging reads otherwise: {Shown(merged)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> MergesIntoABaseCheckoutHoldingAnUntrackedFileAndKeepsItAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(bench.Repository, "notes.txt", "a draft of the developer\n", cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        Check(merged.Match(found => found.Commit.IsSome, _ => false), problems, $"merging into a checkout with only an untracked file reads otherwise: {Shown(merged)}");
        Check(await ReadAsync(bench.Repository, "added.txt", cancellationToken) == "new\n", problems, "the merged work did not reach the base checkout");
        Check(await ReadAsync(bench.Repository, "notes.txt", cancellationToken) == "a draft of the developer\n", problems, "the untracked file did not survive the merge");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> MergesIntoABaseCheckoutHoldingIgnoredBuildOutputAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", Ignored, cancellationToken);
        await WriteAsync(bench.Repository, "bin/Debug/Shop.dll", "built\n", cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        Check(merged.Match(found => found.Commit.IsSome, _ => false), problems, $"merging into a checkout holding ignored build output reads otherwise: {Shown(merged)}");
        Check(await ReadAsync(bench.Repository, "bin/Debug/Shop.dll", cancellationToken) == "built\n", problems, "the ignored build output did not survive the merge");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> LeavesIgnoredBuildOutputOfTheWorkOutOfItsDiffAndItsMergeAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", Ignored, cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "bin/Debug/Shop.dll", "built\n", cancellationToken);
        await WriteAsync(prepared.Path, "obj/project.assets.json", "{}\n", cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.Select(file => file.Path).SequenceEqual(["added.txt"]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files.Select(file => file.Path))))}");

        _ = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        Check(!File.Exists(Path.Combine(bench.Repository, "bin/Debug/Shop.dll")), problems, "the work's ignored build output reached the base checkout");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RestoringACheckpointKeepsIgnoredBuildOutputAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", Ignored, cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        await WriteAsync(prepared.Path, "bin/Debug/Shop.dll", "built\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 2", cancellationToken);

        var listed = await bench.Services.Checkpoints.ListAsync(prepared.Id, cancellationToken);
        Check(listed.Match(found => found.Select(checkpoint => checkpoint.Files.Count).SequenceEqual([1, 0]), _ => false), problems, $"the checkpoints list otherwise: {Shown(listed.Map(found => string.Join("; ", found.Select(checkpoint => $"{checkpoint.Number}: {string.Join(", ", checkpoint.Files.Select(file => file.Path))}"))))}");

        var restored = await bench.Services.Checkpoints.RestoreAsync(prepared.Id, 1, cancellationToken);
        Check(restored.IsSuccess, problems, $"restoring reads otherwise: {Shown(restored)}");
        Check(await ReadAsync(prepared.Path, "bin/Debug/Shop.dll", cancellationToken) == "built\n", problems, "restoring the first checkpoint deleted the ignored build output");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> CountsAndShowsTheLossOfAFinalNewlineAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, Numbers, Tens.TrimEnd('\n'), cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.SequenceEqual([new FileChange(Numbers, ChangeKind.Modified, 1, 1)]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files)))}");

        var file = await bench.Services.Changes.FileDiffAsync(prepared.Id, Numbers, cancellationToken);
        Check(
            file.Match(found => found.Hunks is [{ OldStart: 7, OldLines: 4, NewStart: 7, NewLines: 4 } hunk] && hunk.Lines.TakeLast(2).SequenceEqual([new DiffLine(DiffLineKind.Removed, "ten"), new DiffLine(DiffLineKind.Added, "ten")]), _ => false),
            problems,
            $"the file's diff reads otherwise: {Hunks(file)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> CutsAHunksSectionHeadingAtEightyBytesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var heading = "internal sealed class InMemoryChanges(InMemoryGit git, InMemoryWorkspaces workspaces) : IWorkspaceChanges";
        var body = string.Concat(Enumerable.Range(1, 10).Select(line => $"{line}\n"));
        await bench.CommitToBase("src/Changes.cs", $"{heading}\n{body}", cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "src/Changes.cs", $"{heading}\n{body.Replace("8\n", "eight\n", StringComparison.Ordinal)}", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var file = await bench.Services.Changes.FileDiffAsync(prepared.Id, "src/Changes.cs", cancellationToken);
        Check(file.Match(found => found.Hunks is [{ Section: var section }] && section == heading[..80].TrimEnd(), _ => false), problems, $"the section heading reads otherwise: {Hunks(file)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> TakesOnlyALineStartingWithAnAsciiLetterAsASectionHeadingAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var body = string.Concat(Enumerable.Range(1, 10).Select(line => $"{line}\n"));
        await bench.CommitToBase("docs/notes.md", $"\u00DCberblick\n{body}", cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "docs/notes.md", $"\u00DCberblick\n{body.Replace("8\n", "eight\n", StringComparison.Ordinal)}", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var file = await bench.Services.Changes.FileDiffAsync(prepared.Id, "docs/notes.md", cancellationToken);
        Check(file.Match(found => found.Hunks is [{ Section: "" }], _ => false), problems, $"the section heading reads otherwise: {Hunks(file)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> PlacesAndCountsAnAmbiguousChangeWhereGitDoesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase("src/slid.txt", "b\n", cancellationToken);
        await bench.CommitToBase("src/counted.txt", "b\nb\nb\n  x\n{\n{\n", cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "src/slid.txt", "a\nb\nb\n", cancellationToken);
        await WriteAsync(prepared.Path, "src/counted.txt", "{\nb\n}\nb\nb\n{\n{\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var slid = await bench.Services.Changes.FileDiffAsync(prepared.Id, "src/slid.txt", cancellationToken);
        Check(
            slid.Match(found => found.Hunks is [var hunk] && hunk.Lines.SequenceEqual([new DiffLine(DiffLineKind.Added, "a"), new DiffLine(DiffLineKind.Added, "b"), new DiffLine(DiffLineKind.Context, "b")]), _ => false),
            problems,
            $"the slid change reads otherwise: {Hunks(slid)}");

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.Contains(new FileChange("src/counted.txt", ChangeKind.Modified, 3, 2)), _ => false), problems, $"the counted change reads otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files)))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> SeesAFileTheWorkMadeExecutableAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        var file = Path.Combine(prepared.Path, Numbers);
        File.SetUnixFileMode(file, File.GetUnixFileMode(file) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.SequenceEqual([new FileChange(Numbers, ChangeKind.Modified, 0, 0)]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files)))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> SeesASymbolicLinkTheWorkAddedAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        _ = File.CreateSymbolicLink(Path.Combine(prepared.Path, "latest"), "README.md");
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.SequenceEqual([new FileChange("latest", ChangeKind.Added, 1, 0)]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files)))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsChangedFilesInGitsByteOrderAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "docs/Ａ.md", "fullwidth\n", cancellationToken);
        await WriteAsync(prepared.Path, "docs/\U0001F600.md", "smile\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.Select(file => file.Path).SequenceEqual(["docs/Ａ.md", "docs/\U0001F600.md"]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files.Select(file => file.Path))))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> MergesAChildIntoItsParentsWorktreeHoldingAnUntrackedFileAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(parent.Path, "parent.txt", "the parent's work\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(parent.Id, "Delegated to job 1", cancellationToken);
        var child = (await bench.Services.Workspaces.PrepareAsync(new WorkspaceRequest(bench.Repository, parent.Branch) { Rules = parent.RulesCommit }, cancellationToken))
            .Match(found => found, error => throw new InvalidOperationException($"preparing the child failed with {error}"));
        Check(child.BaseBranch == Option<string>.Some(parent.Branch) && child.RulesCommit == parent.RulesCommit, problems, $"the child names another base: {child}");
        await WriteAsync(child.Path, "child.txt", "the child's work\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(child.Id, "Attempt 1", cancellationToken);
        await WriteAsync(parent.Path, "draft.txt", "the parent kept working\n", cancellationToken);

        var merged = await bench.Services.Changes.MergeAsync(child.Id, "Merge the child", cancellationToken);
        Check(merged.Match(found => found.Commit.IsSome && found.BaseBranch == parent.Branch && found.Checkout.Match(checkout => Same(checkout, parent.Path), () => false), _ => false), problems, $"merging the child reads otherwise: {Shown(merged)}");
        Check(await ReadAsync(parent.Path, "child.txt", cancellationToken) == "the child's work\n", problems, "the child's work did not reach its parent's worktree");
        Check(await ReadAsync(parent.Path, "draft.txt", cancellationToken) == "the parent kept working\n", problems, "the parent's untracked file did not survive the merge");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> NamesAMergeCommitByTheFirstLineOfItsMessageAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        _ = await bench.Services.Changes.MergeAsync(prepared.Id, "Add the file\n\nSquashed by Avala from the checkpoints of 1 attempt.\n\nAvala-Job: 1", cancellationToken);

        var commits = await bench.Index.CommitsAsync(bench.Repository, 1, cancellationToken);
        Check(commits is [{ Subject: "Add the file" }], problems, $"the latest commit reads otherwise: {string.Join(" | ", commits.Select(commit => commit.Subject))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsNoIgnoredFileAmongTheRepositorysFilesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", Ignored, cancellationToken);
        await WriteAsync(bench.Repository, "bin/Debug/Shop.dll", "built\n", cancellationToken);
        await WriteAsync(bench.Repository, "notes.txt", "untracked\n", cancellationToken);

        var files = await bench.Index.FilesAsync(bench.Repository, 100, cancellationToken);
        Check(files.Order(StringComparer.Ordinal).SequenceEqual([".avala/permissions.json", ".gitignore", "README.md", "notes.txt", Numbers]), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsAFileWithAnAccentedNameByItsNameAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await WriteAsync(bench.Repository, "docs/canción.md", "la la\n", cancellationToken);

        var files = await bench.Index.FilesAsync(bench.Repository, 100, cancellationToken);
        Check(files.Contains("docs/canción.md"), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ClonesWhatTheRemoteHoldsAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;

        var cloned = await bench.Remotes.CloneAsync(bench.Repository, parent, "clone", cancellationToken);
        Check(cloned.IsSuccess, problems, $"cloning reads otherwise: {Shown(cloned)}");
        Check(await ReadAsync(Path.Combine(parent, "clone"), "README.md", cancellationToken) == "# Shop\n", problems, "the clone does not hold the remote's files");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToCloneARemoteThatDoesNotExistAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;

        var cloned = await bench.Remotes.CloneAsync(Path.Combine(parent, "missing.git"), parent, "clone", cancellationToken);
        Check(cloned.IsFailure && !Directory.Exists(Path.Combine(parent, "clone")), problems, $"cloning a missing remote reads otherwise: {Shown(cloned)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToPublishToARemoteThatDoesNotExistAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var missing = Path.Combine(Path.GetDirectoryName(bench.Repository)!, "missing.git");

        var published = await bench.Remotes.PublishAsync(bench.Repository, "origin", missing, cancellationToken);
        Check(published.IsFailure, problems, $"publishing to a missing remote reads otherwise: {Shown(published)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> DiffsCrlfEmptyRevertedAndSpacedFilesAsGitDoesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase("src/windows.txt", "one\r\ntwo\r\nthree\r\n", cancellationToken);
        await bench.CommitToBase("old/a.txt", "a\n", cancellationToken);
        await bench.CommitToBase("old/b.txt", "b\nb\n", cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "src/windows.txt", "one\r\nTWO\r\nthree\r\n", cancellationToken);
        await WriteAsync(prepared.Path, "empty.txt", string.Empty, cancellationToken);
        await WriteAsync(prepared.Path, "with space/café menu.txt", "coffee\n", cancellationToken);
        await WriteAsync(prepared.Path, Numbers, "changed\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        await WriteAsync(prepared.Path, Numbers, Tens, cancellationToken);
        Directory.Delete(Path.Combine(prepared.Path, "old"), recursive: true);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 2", cancellationToken);

        var expected = new[]
        {
            new FileChange("empty.txt", ChangeKind.Added, 0, 0),
            new FileChange("old/a.txt", ChangeKind.Deleted, 0, 1),
            new FileChange("old/b.txt", ChangeKind.Deleted, 0, 2),
            new FileChange("src/windows.txt", ChangeKind.Modified, 1, 1),
            new FileChange("with space/café menu.txt", ChangeKind.Added, 1, 0),
        };
        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.SequenceEqual(expected), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files)))}");

        var windows = await bench.Services.Changes.FileDiffAsync(prepared.Id, "src/windows.txt", cancellationToken);
        Check(windows.Match(found => found.Hunks is [var hunk] && hunk.Lines.SequenceEqual([new DiffLine(DiffLineKind.Context, "one\r"), new DiffLine(DiffLineKind.Removed, "two\r"), new DiffLine(DiffLineKind.Added, "TWO\r"), new DiffLine(DiffLineKind.Context, "three\r")]), _ => false), problems, $"the CRLF file's diff reads otherwise: {Hunks(windows)}");

        var empty = await bench.Services.Changes.FileDiffAsync(prepared.Id, "empty.txt", cancellationToken);
        Check(empty.Match(found => found is { Binary: false, Hunks: [] }, _ => false), problems, $"the empty file's diff reads otherwise: {Hunks(empty)}");

        var reverted = await bench.Services.Changes.FileDiffAsync(prepared.Id, Numbers, cancellationToken);
        Check(reverted.Match(_ => false, error => error == WorkspaceFailure.FileUnchanged), problems, $"a file changed and changed back has a diff: {Hunks(reverted)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> MergesTwiceWithOnlyTheNewWorkTheSecondTimeAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, Numbers, Tens.Replace("five\n", "FIVE\n", StringComparison.Ordinal), cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        var first = await bench.Services.Changes.MergeAsync(prepared.Id, "First", cancellationToken);
        await bench.CommitToBase("README.md", "# Shop\n\nMoved on.\n", cancellationToken);
        await WriteAsync(prepared.Path, Numbers, Tens.Replace("five\n", "FIVE\n", StringComparison.Ordinal).Replace("nine\n", "NINE\n", StringComparison.Ordinal), cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 2", cancellationToken);

        var second = await bench.Services.Changes.MergeAsync(prepared.Id, "Second", cancellationToken);
        Check(first.Match(found => found.Commit.IsSome, _ => false) && second.Match(found => found.Commit.IsSome, _ => false), problems, $"merging twice reads otherwise: {Shown(first)} then {Shown(second)}");
        Check(
            await ReadAsync(bench.Repository, Numbers, cancellationToken) == Tens.Replace("five\n", "FIVE\n", StringComparison.Ordinal).Replace("nine\n", "NINE\n", StringComparison.Ordinal)
                && await ReadAsync(bench.Repository, "README.md", cancellationToken) == "# Shop\n\nMoved on.\n",
            problems,
            "the second merge did not keep the base's change and bring the new work");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ReadsTheCurrentFileFromASubfolderAndCommitsInsideAWorktreeAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var current = await bench.Services.BaseFiles.ReadCurrentAsync(Path.Combine(bench.Repository, "src"), Numbers, cancellationToken);
        Check(current.Match(file => file.Content == Option<string>.Some(Tens) && !file.Origin.EditedInWorktree, _ => false), problems, $"reading from a subfolder reads otherwise: {Shown(current)}");

        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, ".avala/checks.json", "{ \"checks\": [] }\n", cancellationToken);
        var committed = await bench.Services.Commits.CommitAsync(prepared.Path, ".avala/checks.json", "Declare the checks", cancellationToken);
        Check(committed.Match(found => found.Branch == prepared.Branch, _ => false), problems, $"committing inside a worktree reads otherwise: {Shown(committed)}");

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.SequenceEqual([new FileChange(".avala/checks.json", ChangeKind.Added, 1, 0)]) && found.Head == committed.Match(made => made.Commit, _ => string.Empty), _ => false), problems, $"the worktree's diff reads otherwise: {Shown(diff)}");

        var inWorktree = await bench.Services.BaseFiles.ReadCurrentAsync(prepared.Path, ".avala/checks.json", cancellationToken);
        Check(inWorktree.Match(file => file.Content.IsSome && !file.Origin.EditedInWorktree, _ => false), problems, $"reading the worktree's current file reads otherwise: {Shown(inWorktree)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> PreparesOnAWorktreeWithItsBranchAsTheBaseAndMergesBackIntoItAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(parent.Path, "parent.txt", "the parent's work\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(parent.Id, "Attempt 1", cancellationToken);

        var nested = (await bench.Services.Workspaces.PrepareAsync(new WorkspaceRequest(parent.Path), cancellationToken))
            .Match(found => found, error => throw new InvalidOperationException($"preparing on the worktree failed with {error}"));
        Check(nested.BaseBranch == Option<string>.Some(parent.Branch), problems, $"a workspace prepared on a worktree names another base branch: {nested.BaseBranch}");
        await WriteAsync(nested.Path, "nested.txt", "the nested work\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(nested.Id, "Attempt 1", cancellationToken);

        var merged = await bench.Services.Changes.MergeAsync(nested.Id, "Merge the nested work", cancellationToken);
        Check(merged.Match(found => found.BaseBranch == parent.Branch && found.Checkout.Match(checkout => Same(checkout, parent.Path), () => false), _ => false), problems, $"merging the nested work reads otherwise: {Shown(merged)}");
        var diff = await bench.Services.Changes.DiffAsync(parent.Id, cancellationToken);
        Check(diff.Match(found => found.Files.Any(file => file.Path == "nested.txt"), _ => false), problems, $"the nested work did not reach the parent's branch: {Shown(diff.Map(found => string.Join(", ", found.Files.Select(file => file.Path))))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> KeepsATrackedFileThatAnIgnoreRuleMatchesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase("logs/keep.log", "kept\n", cancellationToken);
        await bench.CommitToBase(".gitignore", "*.log\n", cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        Check(await ReadAsync(prepared.Path, "logs/keep.log", cancellationToken) == "kept\n", problems, "the worktree does not hold the tracked file an ignore rule matches");
        await WriteAsync(prepared.Path, "logs/keep.log", "kept\nmore\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.SequenceEqual([new FileChange("logs/keep.log", ChangeKind.Modified, 1, 0)]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files)))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> IgnoresWhatACharacterClassNamesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", "*.[oa]\n", cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "build/main.o", "object\n", cancellationToken);
        await WriteAsync(prepared.Path, "build/lib.a", "archive\n", cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.Select(file => file.Path).SequenceEqual(["added.txt"]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files.Select(file => file.Path))))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> IgnoresAPatternWithAnEscapedHashAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", "\\#scratch.txt\n", cancellationToken);
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "#scratch.txt", "scratch\n", cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.Select(file => file.Path).SequenceEqual(["added.txt"]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files.Select(file => file.Path))))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> LeavesAFolderNamedDotGitOutOfTheWorkAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "vendor/.git/config", "[core]\n", cancellationToken);
        await WriteAsync(prepared.Path, "vendor/lib.txt", "library\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        Check(diff.Match(found => found.Files.Select(file => file.Path).SequenceEqual(["vendor/lib.txt"]), _ => false), problems, $"the diff lists otherwise: {Shown(diff.Map(found => string.Join(", ", found.Files.Select(file => file.Path))))}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsUntrackedFilesBeforeTrackedOnesAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await WriteAsync(bench.Repository, "notes.txt", "untracked\n", cancellationToken);

        var files = await bench.Index.FilesAsync(bench.Repository, 100, cancellationToken);
        Check(files.SequenceEqual(["notes.txt", ".avala/permissions.json", "README.md", Numbers]), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ListsATrackedFileDeletedFromTheCheckoutAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        File.Delete(Path.Combine(bench.Repository, "README.md"));

        var files = await bench.Index.FilesAsync(bench.Repository, 100, cancellationToken);
        Check(files.Contains("README.md"), problems, $"the files list otherwise: {string.Join(", ", files)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> KeepsTheBasesExecutableBitWhenMergingAChangeToTheFileAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        var based = Path.Combine(bench.Repository, Numbers);
        File.SetUnixFileMode(based, File.GetUnixFileMode(based) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        await bench.CommitToBase(Numbers, Tens, cancellationToken);
        await WriteAsync(prepared.Path, Numbers, Tens.Replace("ten\n", "TEN\n", StringComparison.Ordinal), cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        Check(merged.Match(found => found.Commit.IsSome, _ => false), problems, $"merging reads otherwise: {Shown(merged)}");
        Check(File.GetUnixFileMode(based).HasFlag(UnixFileMode.UserExecute), problems, "the merge dropped the executable bit the base gave the file");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToMergeOverAnUntrackedFileOfTheBaseCheckoutAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(bench.Repository, "added.txt", "a draft of the developer\n", cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        Check(merged.Match(_ => false, error => error == WorkspaceFailure.BaseCheckoutDirty), problems, $"merging over an untracked file of the base checkout reads otherwise: {Shown(merged)}");
        Check(await ReadAsync(bench.Repository, "added.txt", cancellationToken) == "a draft of the developer\n", problems, "the merge overwrote the developer's untracked file");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> MergesAChangeIntoAFileTheBaseRenamedAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        File.Delete(Path.Combine(bench.Repository, Numbers));
        await bench.CommitToBase("src/counted.txt", Tens, cancellationToken);
        await WriteAsync(prepared.Path, Numbers, Tens.Replace("ten\n", "TEN\n", StringComparison.Ordinal), cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);

        var merged = await bench.Services.Changes.MergeAsync(prepared.Id, "Merge", cancellationToken);
        Check(merged.Match(found => found.Commit.IsSome, _ => false), problems, $"merging a change into a file the base renamed reads otherwise: {Shown(merged)}");
        Check(await ReadAsync(bench.Repository, "src/counted.txt", cancellationToken) == Tens.Replace("ten\n", "TEN\n", StringComparison.Ordinal), problems, "the renamed file does not hold the work's change");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToCloneAFolderThatIsNotARepositoryAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;
        var plain = Path.Combine(parent, "plain");
        await WriteAsync(plain, "notes.txt", "not a repository\n", cancellationToken);

        var cloned = await bench.Remotes.CloneAsync(plain, parent, "clone", cancellationToken);
        Check(cloned.IsFailure && !Directory.Exists(Path.Combine(parent, "clone")), problems, $"cloning a folder that is not a repository reads otherwise: {Shown(cloned)}");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> ClonesTheRemotesHistoryAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var parent = Path.GetDirectoryName(bench.Repository)!;

        _ = await bench.Remotes.CloneAsync(bench.Repository, parent, "clone", cancellationToken);
        var original = await bench.Index.CommitsAsync(bench.Repository, 10, cancellationToken);
        var cloned = await bench.Index.CommitsAsync(Path.Combine(parent, "clone"), 10, cancellationToken);
        Check(cloned.Select(commit => commit.Subject).SequenceEqual(original.Select(commit => commit.Subject)), problems, $"the clone's history reads {string.Join(" | ", cloned.Select(commit => commit.Subject))} where the remote's reads {string.Join(" | ", original.Select(commit => commit.Subject))}");

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
        var based = Path.Combine(bench.Repository, Numbers);
        File.SetUnixFileMode(based, File.GetUnixFileMode(based) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        await bench.CommitToBase(Numbers, Tens, cancellationToken);

        _ = await bench.Remotes.CloneAsync(bench.Repository, parent, "clone", cancellationToken);
        Check(File.GetUnixFileMode(Path.Combine(parent, "clone", Numbers)).HasFlag(UnixFileMode.UserExecute), problems, "the clone does not keep the remote's executable bit");

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
        Check(initialized.IsSuccess, problems, $"initializing reads otherwise: {Shown(initialized)}");
        Check((await File.ReadAllBytesAsync(Path.Combine(plain, "logo.png"), cancellationToken)).AsSpan().SequenceEqual(picture), problems, "initializing the folder rewrote its binary file");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RestoresACheckpointWhereAFolderWasStillAFileAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "notes", "draft\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        File.Delete(Path.Combine(prepared.Path, "notes"));
        await WriteAsync(prepared.Path, "notes/today.md", "today\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 2", cancellationToken);

        var restored = await bench.Services.Checkpoints.RestoreAsync(prepared.Id, 1, cancellationToken);
        Check(restored.IsSuccess, problems, $"restoring reads otherwise: {Shown(restored)}");
        Check(await ReadAsync(prepared.Path, "notes", cancellationToken) == "draft\n", problems, "restoring did not bring the file back where the folder was");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RestoringACheckpointRemovesTheFolderItEmptiedAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var prepared = await PrepareAsync(bench, cancellationToken);
        await WriteAsync(prepared.Path, "added.txt", "new\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        await WriteAsync(prepared.Path, "old/a.txt", "a\n", cancellationToken);
        _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 2", cancellationToken);

        var restored = await bench.Services.Checkpoints.RestoreAsync(prepared.Id, 1, cancellationToken);
        Check(restored.IsSuccess, problems, $"restoring reads otherwise: {Shown(restored)}");
        Check(!Directory.Exists(Path.Combine(prepared.Path, "old")), problems, "restoring left behind the folder it emptied");

        return problems;
    }

    private static async Task<IReadOnlyList<string>> RefusesToPublishToAFolderThatIsNotARepositoryAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var plain = Path.Combine(Path.GetDirectoryName(bench.Repository)!, "plain");
        await WriteAsync(plain, "notes.txt", "not a repository\n", cancellationToken);

        var published = await bench.Remotes.PublishAsync(bench.Repository, "origin", plain, cancellationToken);
        Check(published.IsFailure, problems, $"publishing to a folder that is not a repository reads otherwise: {Shown(published)}");

        return problems;
    }

    private static async Task<WorkspaceInfo> PrepareAsync(FidelityBench bench, CancellationToken cancellationToken) =>
        (await bench.Services.Workspaces.PrepareAsync(new WorkspaceRequest(bench.Repository), cancellationToken))
            .Match(found => found, error => throw new InvalidOperationException($"preparing failed with {error}"));

    private static async Task WriteAsync(string folder, string path, string content, CancellationToken cancellationToken)
    {
        var file = Path.Combine(folder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, Encoding.UTF8.GetBytes(content), cancellationToken);
    }

    private static async Task<string> ReadAsync(string folder, string path, CancellationToken cancellationToken) =>
        File.Exists(Path.Combine(folder, path)) ? await File.ReadAllTextAsync(Path.Combine(folder, path), cancellationToken) : string.Empty;

    private static string Hunks(Result<FileDiff, WorkspaceFailure> file) =>
        Shown(file.Map(found => string.Join(" | ", found.Hunks.Select(hunk => $"{hunk.OldStart},{hunk.OldLines} {hunk.NewStart},{hunk.NewLines} '{hunk.Section}' {string.Join("/", hunk.Lines.Select(line => $"{line.Kind}:{line.Text}"))}"))));

    private static bool Same(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));

    private static string Shown<T>(Result<T, WorkspaceFailure> result) => result.Match(found => found?.ToString() ?? string.Empty, error => error.ToString());

    private static void Check(bool holds, List<string> problems, string problem)
    {
        if (!holds)
        {
            problems.Add(problem);
        }
    }
}
