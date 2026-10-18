using System.Collections.Immutable;
using System.Text.Json;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal sealed class TreeLedger(AvalaPaths paths) : IAsyncDisposable
{
    public const string FileName = "process-trees.json";

    private readonly SerialExecutor owner = new();
    private ImmutableHashSet<ProcessTreeId>? earlier;

    private string File => Path.Combine(paths.Data, FileName);

    public Task<IReadOnlyList<ProcessTreeId>> EarlierAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<ProcessTreeId>>(async token => [.. await EarlierOwnedAsync(token)], cancellationToken);

    public Task KeepAsync(Func<IEnumerable<ProcessTreeId>> open, CancellationToken cancellationToken) =>
        owner.RunAsync(async token => await WriteAsync(await EarlierOwnedAsync(token), open(), token), cancellationToken);

    public Task ReapedAsync(ProcessTreeId tree, Func<IEnumerable<ProcessTreeId>> open, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async token =>
            {
                earlier = (await EarlierOwnedAsync(token)).Remove(tree);
                await WriteAsync(earlier, open(), token);
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task<ImmutableHashSet<ProcessTreeId>> EarlierOwnedAsync(CancellationToken cancellationToken) =>
        earlier ??= await ReadAsync(cancellationToken);

    private async Task<ImmutableHashSet<ProcessTreeId>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            var ids = await JsonSerializer.DeserializeAsync<Guid[]>(stream, cancellationToken: cancellationToken) ?? [];

            return [.. ids.Select(id => new ProcessTreeId(id))];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private async Task WriteAsync(IEnumerable<ProcessTreeId> earlierTrees, IEnumerable<ProcessTreeId> open, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(paths.Data);
            await using var stream = new FileStream(File, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, earlierTrees.Concat(open).Select(tree => tree.Value).Distinct().ToArray(), cancellationToken: cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }
}
