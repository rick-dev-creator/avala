namespace Avala.CommandLines;

public sealed record CommandLine(IReadOnlyList<ShellCommand> Commands, IReadOnlyList<string> Writes, bool Opaque, bool MovesDirectory)
{
    public IReadOnlyList<string> Reads { get; init; } = [];

    public IReadOnlyList<string> Landings { get; init; } = [];

    public IReadOnlyList<string> Adrift { get; init; } = [];

    public static CommandLine Parse(string text) => new ShellReader(text).Read();

    public static bool Rooted(string path) => path.StartsWith('/') || path.StartsWith('\\') || (path.Length > 1 && path[1] == ':');

    public static string Within(string directory, string path) =>
        Rooted(path) || directory.Length == 0 ? path : $"{directory.TrimEnd('/', '\\')}/{path}";
}

public sealed record ShellCommand(string Text, IReadOnlyList<string> Words, IReadOnlyList<string> Writes, bool Restated)
{
    public IReadOnlyList<string> Raws { get; init; } = [];
}
