using Avala.Sdk;

namespace Avala.Recording.Recordings;

internal sealed record Redaction(string WorkingDirectory, IReadOnlyList<string> Secrets)
{
    public const string WorkingDirectoryMark = "${workingDirectory}";

    public const string SecretMark = "[redacted]";

    public string Apply(string text) =>
        Secrets.Aggregate(text.Marking(WorkingDirectory, WorkingDirectoryMark), (redacted, secret) => redacted.Marking(secret, SecretMark));
}
