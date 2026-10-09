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

    [Fact]
    public void AWindowsPathUnderAMarkIsWrittenWithPortableSeparators() =>
        Assert.Equal(
            "Edit ${workingDirectory}/docs/notes.md, plan saved at: [redacted]/.claude-work/plans/greeting.md and \"${workingDirectory}/GREETING.md\" kept C:\\Other\\file.md",
            new Redaction("C:\\Users\\ana\\worktrees\\job-1", ["C:\\Users\\ana\\home"])
                .Apply("Edit C:\\Users\\ana\\worktrees\\job-1\\docs\\notes.md, plan saved at: C:\\Users\\ana\\home\\.claude-work/plans/greeting.md and \"C:\\Users\\ana\\worktrees\\job-1\\GREETING.md\" kept C:\\Other\\file.md"));
}
