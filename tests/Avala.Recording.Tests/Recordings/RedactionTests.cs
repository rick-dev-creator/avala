using Avala.Recording.Recordings;

namespace Avala.Recording.Tests.Recordings;

public sealed class RedactionTests
{
    [Fact]
    public void TheWorkingDirectoryBecomesAMarkAndEveryListedSecretIsRedacted() =>
        Assert.Equal(
            "Edit ${workingDirectory}/notes.md for [redacted] with key [redacted]",
            new Redaction("/home/ana/worktrees/job-1", ["ana@example.com", "sk-123"])
                .Apply("Edit /home/ana/worktrees/job-1/notes.md for ana@example.com with key sk-123"));
}
