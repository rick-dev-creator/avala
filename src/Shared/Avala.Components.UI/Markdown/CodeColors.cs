using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Avala.Sdk;

namespace Avala.Components.UI.Markdown;

public sealed record CodeSpan(string Text, string Role);

public static class CodeColors
{
    public const int HighlightedLines = 300;

    public const int ChunkLines = 60;

    public const long CacheBudget = 8L * 1024 * 1024;

    private static readonly SerialExecutor Owner = new();
    private static readonly LinkedList<(string Language, string Code, long Bytes)> Recent = [];
    private static long cachedBytes;
    private static ImmutableDictionary<(string Language, string Code), IReadOnlyList<IReadOnlyList<CodeSpan>>> colored =
        ImmutableDictionary<(string Language, string Code), IReadOnlyList<IReadOnlyList<CodeSpan>>>.Empty;

    public static long CachedBytes => Volatile.Read(ref cachedBytes);

    public static int Count(string code) => code.Count(character => character == '\n') + 1;

    public static Option<IReadOnlyList<IReadOnlyList<CodeSpan>>> Cached(string code, string language) =>
        Volatile.Read(ref colored).TryGetValue((Key(language), code), out var lines)
            ? Option<IReadOnlyList<IReadOnlyList<CodeSpan>>>.Some(lines)
            : Option<IReadOnlyList<IReadOnlyList<CodeSpan>>>.None;

    public static async Task<IReadOnlyList<IReadOnlyList<CodeSpan>>> LinesAsync(string code, string language, CancellationToken cancellationToken)
    {
        var lines = new List<IReadOnlyList<CodeSpan>>();

        await foreach (var chunk in ColorAsync(code, language, cancellationToken))
        {
            lines.AddRange(chunk);
        }

        return lines;
    }

    public static async IAsyncEnumerable<IReadOnlyList<IReadOnlyList<CodeSpan>>> ColorAsync(
        string code,
        string language,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var key = (Key(language), code);

        if (Volatile.Read(ref colored).TryGetValue(key, out var known))
        {
            yield return known;

            yield break;
        }

        var lines = code.Split('\n');
        var all = new List<IReadOnlyList<CodeSpan>>(lines.Length);
        var coloring = new CodeTokens(key.Item1);

        for (var start = 0; start < lines.Length; start += ChunkLines)
        {
            var chunk = lines[start..Math.Min(lines.Length, start + ChunkLines)];
            var last = start + ChunkLines >= lines.Length;

            yield return await Owner.RunAsync(
                _ =>
                {
                    var spans = coloring.Next(chunk);
                    all.AddRange(spans);

                    if (last)
                    {
                        Remember(key, all);
                    }

                    return Task.FromResult(spans);
                },
                cancellationToken);
        }
    }

    private static string Key(string language) => language.Trim().ToLowerInvariant();

    private static long Size(string code, IReadOnlyList<IReadOnlyList<CodeSpan>> lines) =>
        (code.Length * 4L) + (lines.Sum(line => line.Count) * 64L);

    private static void Remember((string Language, string Code) key, IReadOnlyList<IReadOnlyList<CodeSpan>> lines)
    {
        var current = Volatile.Read(ref colored);

        if (current.ContainsKey(key))
        {
            return;
        }

        var bytes = Size(key.Code, lines);
        current = current.SetItem(key, lines);
        Recent.AddLast((key.Language, key.Code, bytes));
        Volatile.Write(ref cachedBytes, cachedBytes + bytes);

        while (cachedBytes > CacheBudget && Recent.First is { } oldest)
        {
            Recent.RemoveFirst();
            Volatile.Write(ref cachedBytes, cachedBytes - oldest.Value.Bytes);
            current = current.Remove((oldest.Value.Language, oldest.Value.Code));
        }

        Volatile.Write(ref colored, current);
    }

}
