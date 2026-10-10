using Avala.ArchitectureTests.Source;

namespace Avala.ArchitectureTests.SelfTests;

public sealed class TrackedFilesTests
{
    [Fact]
    public void FlagsTrackedFilesOfThePrivateMethod() =>
        Assert.Equal(
            [".method/AGENTS.md", ".method/sessions/pricing.json"],
            TrackedFiles.InPrivateMethod([".method/AGENTS.md", "AGENTS.md", ".method/sessions/pricing.json", "docs/.method/notes.md", ".methodology/plan.md", ".gitignore"]));
}
