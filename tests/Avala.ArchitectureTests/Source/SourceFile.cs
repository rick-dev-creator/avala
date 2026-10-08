namespace Avala.ArchitectureTests.Source;

internal sealed record SourceFile(string Path, string Text)
{
    public static async Task<IReadOnlyList<SourceFile>> ReadAllAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken) =>
        await Task.WhenAll(paths.Select(async path =>
            new SourceFile(path, await File.ReadAllTextAsync(path, cancellationToken))));
}
