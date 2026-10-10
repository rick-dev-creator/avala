namespace Avala.CommandLines;

public sealed record CommandLine(IReadOnlyList<ShellCommand> Commands, IReadOnlyList<string> Writes, bool Opaque, bool MovesDirectory)
{
    public static CommandLine Parse(string text) => new ShellReader(text).Read();

    public static bool Rooted(string path) => path.StartsWith('/') || path.StartsWith('\\') || (path.Length > 1 && path[1] == ':');
}

public sealed record ShellCommand(string Text, IReadOnlyList<string> Words, IReadOnlyList<string> Writes, bool Restated);
