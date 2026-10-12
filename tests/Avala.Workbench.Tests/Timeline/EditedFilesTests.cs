using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Timeline;

public sealed class EditedFilesTests
{
    [Theory]
    [InlineData(ItemKind.FileEdit, "Write sistema-solar.html", "sistema-solar.html")]
    [InlineData(ItemKind.FileEdit, "Edit src/money/minor.go", "src/money/minor.go")]
    [InlineData(ItemKind.FileEdit, "Edit src/a very long path that was cut…", "")]
    [InlineData(ItemKind.Command, "Write notes.md", "")]
    [InlineData(ItemKind.FileEdit, "Writing", "")]
    public void AFileEditNamesThePathItsTitleCarries(ItemKind kind, string title, string path) =>
        Assert.Equal(path, new ToolEntry("t", kind, title, string.Empty, Option<ItemOutcome>.None).EditedPath.Match(found => found, () => string.Empty));
}
