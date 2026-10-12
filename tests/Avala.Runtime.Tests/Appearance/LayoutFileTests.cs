using Avala.Runtime.Appearance;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Testing;

namespace Avala.Runtime.Tests.Appearance;

public sealed class LayoutFileTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACollapsedSidebarIsWrittenNextToTheAppearanceAndReadBackByTheNextStartAsync()
    {
        await using var data = new TemporaryFolder();
        await using (var file = new LayoutFile(new AvalaPaths(data.Path)))
        {
            await file.RememberAsync(new WindowLayout(true), Cancellation);
        }

        await using var restarted = new LayoutFile(new AvalaPaths(data.Path));

        Assert.Equal(new WindowLayout(true), await restarted.ReadAsync(Cancellation));
        Assert.Equal("{\n  \"sidebar\": \"collapsed\"\n}\n", (await File.ReadAllTextAsync(Path.Combine(data.Path, LayoutFile.FileName), Cancellation)).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "sidebar": "shown" }""")]
    [InlineData("""{ "sidebar": true }""")]
    [InlineData("""[ "collapsed" ]""")]
    [InlineData("not json")]
    public async Task WithoutARememberedCollapseTheSidebarShowsAsync(string? content)
    {
        await using var data = new TemporaryFolder();

        if (content is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(data.Path, LayoutFile.FileName), content, Cancellation);
        }

        await using var file = new LayoutFile(new AvalaPaths(data.Path));

        Assert.Equal(WindowLayout.Default, await file.ReadAsync(Cancellation));
    }
}
