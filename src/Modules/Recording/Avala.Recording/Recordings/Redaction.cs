namespace Avala.Recording.Recordings;

internal sealed record Redaction(string WorkingDirectory, IReadOnlyList<string> Secrets)
{
    public const string WorkingDirectoryMark = "${workingDirectory}";

    public const string SecretMark = "[redacted]";

    public string Apply(string text) =>
        Secrets.Aggregate(
            WorkingDirectory.Length == 0 ? text : text.Replace(WorkingDirectory, WorkingDirectoryMark, StringComparison.Ordinal),
            (redacted, secret) => redacted.Replace(secret, SecretMark, StringComparison.Ordinal));
}
