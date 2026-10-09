namespace Avala.Verification.Checks;

internal sealed record DeclaredCheck(string Name, string Command, IReadOnlyList<string> Arguments, TimeSpan Timeout)
{
    public string CommandLine => string.Join(' ', [Command, .. Arguments.Select(Quoted)]);

    private static string Quoted(string argument) =>
        argument.Length == 0 || argument.Any(character => char.IsWhiteSpace(character) || character == '"')
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
}
