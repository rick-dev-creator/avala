using Avala.Components.UI.Copying;
using Avala.Components.UI.Folders;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Components.UI.Tests;

public sealed class DesktopDialogsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutADesktopWindowNothingIsCopiedAsync() =>
        Assert.False(await new AvaloniaClipboard().CopyAsync("git push", Cancellation));

    [Fact]
    public async Task WithoutADesktopWindowSavingIsUnavailableAsync() =>
        Assert.Equal(FileSaveError.Unavailable, Outcomes.FailsWith(await new AvaloniaFileSaver().SaveAsync("Export", "transcript.md", "# Notes", Cancellation)));

    [Fact]
    public async Task WithoutADesktopWindowNoFolderIsPickedAsync() =>
        Assert.True((await new AvaloniaFolderPicker().PickAsync("Choose a repository", "~/projects", Cancellation)).IsNone);
}
