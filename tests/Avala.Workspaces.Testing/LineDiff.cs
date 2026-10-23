using System.Text;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

internal static class LineDiff
{
    private const int Context = 3;

    private const int BinaryProbe = 8000;

    private const int SectionBytes = 80;

    public static bool IsBinary(byte[] content) => content.AsSpan(0, Math.Min(content.Length, BinaryProbe)).Contains((byte)0);

    public static Option<FileChange> Change(string path, FolderSnapshot before, FolderSnapshot after)
    {
        var (old, current) = (before.Entry(path), after.Entry(path));

        if (FolderSnapshot.SameEntry(before, after, path) || (old.IsNone && current.IsNone))
        {
            return Option<FileChange>.None;
        }

        var kind = old.IsNone ? ChangeKind.Added : current.IsNone ? ChangeKind.Deleted : ChangeKind.Modified;
        var (oldBytes, newBytes) = (old.Match(found => found, () => []), current.Match(found => found, () => []));

        if (IsBinary(oldBytes) || IsBinary(newBytes))
        {
            return new FileChange(path, kind, Option<int>.None, Option<int>.None);
        }

        var edits = Edits(Lines(oldBytes), Lines(newBytes));

        return new FileChange(path, kind, edits.Count(edit => edit.Kind == DiffLineKind.Added), edits.Count(edit => edit.Kind == DiffLineKind.Removed));
    }

    public static Option<FileDiff> Of(string path, FolderSnapshot before, FolderSnapshot after)
    {
        if (Change(path, before, after).IsNone)
        {
            return Option<FileDiff>.None;
        }

        var (old, current) = (before.Entry(path).Match(found => found, () => []), after.Entry(path).Match(found => found, () => []));

        if (IsBinary(old) || IsBinary(current))
        {
            return new FileDiff(path, Binary: true, []);
        }

        var oldLines = Lines(old);

        return new FileDiff(path, Binary: false, Hunks(oldLines, Edits(oldLines, Lines(current))));
    }

    public static Option<byte[]> Merge(byte[] ancestor, byte[] ours, byte[] theirs)
    {
        if (IsBinary(ancestor) || IsBinary(ours) || IsBinary(theirs))
        {
            return Option<byte[]>.None;
        }

        var lines = Lines(ancestor);
        var (mine, other) = (Chunks(lines, Lines(ours)), Chunks(lines, Lines(theirs)));

        if (mine.Any(chunk => other.Any(against => chunk.Start <= against.End && against.Start <= chunk.End && chunk != against)))
        {
            return Option<byte[]>.None;
        }

        var merged = new StringBuilder();
        var position = 0;

        foreach (var chunk in mine.Union(other).OrderBy(chunk => chunk.Start).ThenBy(chunk => chunk.End))
        {
            if (chunk.Start < position)
            {
                continue;
            }

            merged.Append(string.Concat(lines[position..chunk.Start])).Append(string.Concat(chunk.Replacement));
            position = chunk.End;
        }

        return Encoding.UTF8.GetBytes(merged.Append(string.Concat(lines[position..])).ToString());
    }

    private static List<Chunk> Chunks(string[] old, string[] current)
    {
        var chunks = new List<Chunk>();
        var edits = Edits(old, current);

        for (var index = 0; index < edits.Count;)
        {
            if (edits[index].Kind == DiffLineKind.Context)
            {
                index++;
                continue;
            }

            var start = edits[index].Old;
            var added = new List<string>();
            var removed = 0;

            for (; index < edits.Count && edits[index].Kind != DiffLineKind.Context; index++)
            {
                removed += edits[index].Kind == DiffLineKind.Removed ? 1 : 0;
                added.AddRange(edits[index].Kind == DiffLineKind.Added ? [edits[index].Line] : []);
            }

            chunks.Add(new Chunk(start, start + removed, [.. added]));
        }

        return chunks;
    }

    private static string[] Lines(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        var lines = new List<string>();
        var start = 0;

        for (var index = text.IndexOf('\n'); index >= 0; index = text.IndexOf('\n', start))
        {
            lines.Add(text[start..(index + 1)]);
            start = index + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return [.. lines];
    }

    private static List<Edit> Edits(string[] old, string[] current)
    {
        var trace = Trace(old, current);
        var edits = new List<Edit>();
        var (x, y) = (old.Length, current.Length);

        for (var depth = trace.Count - 1; depth >= 0 && (x > 0 || y > 0); depth--)
        {
            var (down, startX, startY) = Previous(trace[depth], old.Length + current.Length, x - y, depth);

            while (x > startX && y > startY)
            {
                edits.Add(new Edit(DiffLineKind.Context, x - 1, y - 1, old[x - 1]));
                (x, y) = (x - 1, y - 1);
            }

            if (depth > 0)
            {
                edits.Add(down
                    ? new Edit(DiffLineKind.Added, x, y - 1, current[y - 1])
                    : new Edit(DiffLineKind.Removed, x - 1, y, old[x - 1]));
            }

            (x, y) = (startX, startY);
        }

        edits.Reverse();

        return edits;
    }

    private static (bool Down, int X, int Y) Previous(int[] frontier, int offset, int diagonal, int depth)
    {
        var down = diagonal == -depth || (diagonal != depth && frontier[offset + diagonal - 1] < frontier[offset + diagonal + 1]);
        var previous = down ? diagonal + 1 : diagonal - 1;
        var x = frontier[offset + previous];

        return (down, x, x - previous);
    }

    private static List<int[]> Trace(string[] old, string[] current)
    {
        var offset = old.Length + current.Length;
        var frontier = new int[(2 * offset) + 2];
        var trace = new List<int[]>();

        for (var depth = 0; depth <= offset; depth++)
        {
            trace.Add((int[])frontier.Clone());

            for (var diagonal = -depth; diagonal <= depth; diagonal += 2)
            {
                var x = Furthest(frontier, offset, diagonal, depth, old, current);
                frontier[offset + diagonal] = x;

                if (x >= old.Length && x - diagonal >= current.Length)
                {
                    return trace;
                }
            }
        }

        return trace;
    }

    private static int Furthest(int[] frontier, int offset, int diagonal, int depth, string[] old, string[] current)
    {
        var x = diagonal == -depth || (diagonal != depth && frontier[offset + diagonal - 1] < frontier[offset + diagonal + 1])
            ? frontier[offset + diagonal + 1]
            : frontier[offset + diagonal - 1] + 1;
        var y = x - diagonal;

        while (x < old.Length && y < current.Length && string.Equals(old[x], current[y], StringComparison.Ordinal))
        {
            (x, y) = (x + 1, y + 1);
        }

        return x;
    }

    private static List<DiffHunk> Hunks(string[] old, List<Edit> edits)
    {
        var changed = edits.Select((edit, index) => (edit, index)).Where(pair => pair.edit.Kind != DiffLineKind.Context).Select(pair => pair.index).ToList();
        var hunks = new List<DiffHunk>();
        var start = 0;

        while (start < changed.Count)
        {
            var end = start;

            while (end + 1 < changed.Count && changed[end + 1] - changed[end] <= (2 * Context) + 1)
            {
                end++;
            }

            var first = Math.Max(0, changed[start] - Context);
            var last = Math.Min(edits.Count - 1, changed[end] + Context);
            hunks.Add(Hunk(old, edits.GetRange(first, last - first + 1)));
            start = end + 1;
        }

        return hunks;
    }

    private static DiffHunk Hunk(string[] old, List<Edit> lines)
    {
        var oldLines = lines.Count(line => line.Kind != DiffLineKind.Added);
        var newLines = lines.Count(line => line.Kind != DiffLineKind.Removed);
        var oldStart = lines[0].Old + (oldLines == 0 ? 0 : 1);
        var newStart = lines[0].New + (newLines == 0 ? 0 : 1);

        return new DiffHunk(oldStart, oldLines, newStart, newLines, Section(old, lines[0].Old), [.. lines.Select(line => new DiffLine(line.Kind, line.Text))]);
    }

    private static string Section(string[] old, int before)
    {
        var line = old.Take(Math.Min(before, old.Length)).LastOrDefault(found => found.Length > 0 && (char.IsAsciiLetter(found[0]) || found[0] is '_' or '$'));

        if (line is null)
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(line.TrimEnd('\n', '\r'));

        return Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, SectionBytes)).TrimEnd();
    }

    private sealed record Edit(DiffLineKind Kind, int Old, int New, string Line)
    {
        public string Text => Line.EndsWith('\n') ? Line[..^1] : Line;
    }

    private sealed record Chunk(int Start, int End, string[] Replacement)
    {
        public bool Equals(Chunk? other) => other is not null && Start == other.Start && End == other.End && Replacement.SequenceEqual(other.Replacement);

        public override int GetHashCode() => HashCode.Combine(Start, End, Replacement.Length);
    }
}
