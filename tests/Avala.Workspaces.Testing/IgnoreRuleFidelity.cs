namespace Avala.Workspaces.Testing;

public static class IgnoreRuleFidelity
{
    public static IReadOnlyDictionary<string, Func<FidelityBench, CancellationToken, Task<IReadOnlyList<string>>>> Cases { get; } =
        new Dictionary<string, Func<FidelityBench, CancellationToken, Task<IReadOnlyList<string>>>>(StringComparer.Ordinal)
        {
            [nameof(IgnoresByAPosixCharacterClassAsync)] = IgnoresByAPosixCharacterClassAsync,
            [nameof(ANegatedCharacterClassNeverMatchesASlashAsync)] = ANegatedCharacterClassNeverMatchesASlashAsync,
            [nameof(ReadsAnIgnoreRuleWhoseClassNeverClosesAsync)] = ReadsAnIgnoreRuleWhoseClassNeverClosesAsync,
            [nameof(ANestedIgnoreFileOverridesTheRootsEvenInAFolderNamedWithADashAsync)] = ANestedIgnoreFileOverridesTheRootsEvenInAFolderNamedWithADashAsync,
            [nameof(TwoStarsBesideANameMatchWithinOneFolderOnlyAsync)] = TwoStarsBesideANameMatchWithinOneFolderOnlyAsync,
            [nameof(AnUnclosedBracketMatchesNothingAsync)] = AnUnclosedBracketMatchesNothingAsync,
            [nameof(ATrailingBackslashMatchesNothingAsync)] = ATrailingBackslashMatchesNothingAsync,
        };

    private static async Task<IReadOnlyList<string>> IgnoresByAPosixCharacterClassAsync(FidelityBench bench, CancellationToken cancellationToken) =>
        await ListsAsync(bench, "log[[:digit:]].txt\n", ["log1.txt", "logd.txt"], ["logd.txt"], cancellationToken);

    private static async Task<IReadOnlyList<string>> ANegatedCharacterClassNeverMatchesASlashAsync(FidelityBench bench, CancellationToken cancellationToken) =>
        await ListsAsync(bench, "log[!s]old\n", ["log/old", "logXold"], ["log/old"], cancellationToken);

    private static async Task<IReadOnlyList<string>> ReadsAnIgnoreRuleWhoseClassNeverClosesAsync(FidelityBench bench, CancellationToken cancellationToken) =>
        await ListsAsync(bench, "[a\\]x.txt\n", ["kept.txt"], ["kept.txt"], cancellationToken);

    private static async Task<IReadOnlyList<string>> ANestedIgnoreFileOverridesTheRootsEvenInAFolderNamedWithADashAsync(FidelityBench bench, CancellationToken cancellationToken)
    {
        await bench.CommitToBase("-legacy/.gitignore", "!*.log\n", cancellationToken);

        return await ListsAsync(bench, "*.log\n", ["-legacy/kept.log", "dropped.log"], ["-legacy/kept.log"], cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> TwoStarsBesideANameMatchWithinOneFolderOnlyAsync(FidelityBench bench, CancellationToken cancellationToken) =>
        await ListsAsync(bench, "src/**.tmp\n", ["src/a.tmp", "src/deep/b.tmp"], ["src/deep/b.tmp"], cancellationToken);

    private static async Task<IReadOnlyList<string>> AnUnclosedBracketMatchesNothingAsync(FidelityBench bench, CancellationToken cancellationToken) =>
        await ListsAsync(bench, "notes[\n", ["notes["], ["notes["], cancellationToken);

    private static async Task<IReadOnlyList<string>> ATrailingBackslashMatchesNothingAsync(FidelityBench bench, CancellationToken cancellationToken) =>
        await ListsAsync(bench, "notes\\\n", ["notes "], ["notes "], cancellationToken);

    private static async Task<IReadOnlyList<string>> ListsAsync(FidelityBench bench, string rules, IReadOnlyList<string> written, IReadOnlyList<string> listed, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await bench.CommitToBase(".gitignore", rules, cancellationToken);
        var prepared = await WorkspaceFidelity.PrepareAsync(bench, cancellationToken);

        foreach (var file in written)
        {
            await WorkspaceFidelity.WriteAsync(prepared.Path, file, "content\n", cancellationToken);
        }

        try
        {
            _ = await bench.Services.Workspaces.CheckpointAsync(prepared.Id, "Attempt 1", cancellationToken);
        }
        catch (ArgumentException exception)
        {
            problems.Add($"checkpointing threw {exception.GetType().Name}: {exception.Message}");

            return problems;
        }

        var diff = await bench.Services.Changes.DiffAsync(prepared.Id, cancellationToken);
        WorkspaceFidelity.Check(
            diff.Match(found => found.Files.Select(file => file.Path).Where(path => written.Contains(path)).SequenceEqual(listed), _ => false),
            problems,
            $"with the rules {rules.Trim()} the diff lists otherwise: {WorkspaceFidelity.Shown(diff.Map(found => string.Join(", ", found.Files.Select(file => file.Path))))}");

        return problems;
    }
}
