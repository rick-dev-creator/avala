using Avala.Runtime.Appearance;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Testing;

namespace Avala.Runtime.Tests.Appearance;

public sealed class LayoutFileTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACollapsedSidebarAndItsWidthAreWrittenNextToTheAppearanceAndReadBackByTheNextStartAsync()
    {
        await using var data = new TemporaryFolder();
        await using (var file = new LayoutFile(new AvalaPaths(data.Path)))
        {
            await file.RememberAsync(_ => new WindowLayout(true, 312), Cancellation);
        }

        await using var restarted = new LayoutFile(new AvalaPaths(data.Path));

        Assert.Equal(new WindowLayout(true, 312), await restarted.ReadAsync(Cancellation));
        Assert.Equal("{\n  \"sidebar\": \"collapsed\",\n  \"sidebarWidth\": 312,\n  \"previewShare\": 0.45\n}\n", (await File.ReadAllTextAsync(Path.Combine(data.Path, LayoutFile.FileName), Cancellation)).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "sidebar": "shown" }""")]
    [InlineData("""{ "sidebar": "shown", "sidebarWidth": "wide" }""")]
    [InlineData("""{ "sidebar": true }""")]
    [InlineData("""[ "collapsed" ]""")]
    [InlineData("not json")]
    public async Task WithoutARememberedLayoutTheSidebarShowsAtItsDefaultWidthAsync(string? content)
    {
        await using var data = new TemporaryFolder();

        if (content is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(data.Path, LayoutFile.FileName), content, Cancellation);
        }

        await using var file = new LayoutFile(new AvalaPaths(data.Path));

        Assert.Equal(WindowLayout.Default, await file.ReadAsync(Cancellation));
    }

    [Fact]
    public async Task ThePreviewsShareOfTheJobIsRememberedBesideTheSidebarAsync()
    {
        await using var data = new TemporaryFolder();
        await using (var file = new LayoutFile(new AvalaPaths(data.Path)))
        {
            await file.RememberAsync(_ => new WindowLayout(true, 312), Cancellation);
            await file.RememberAsync(remembered => remembered with { PreviewShare = 0.6 }, Cancellation);
        }

        await using var restarted = new LayoutFile(new AvalaPaths(data.Path));

        Assert.Equal(new WindowLayout(true, 312) { PreviewShare = 0.6 }, await restarted.ReadAsync(Cancellation));
    }

    [Theory]
    [InlineData(0.05, WindowLayout.SmallestPreviewShare)]
    [InlineData(0.95, WindowLayout.LargestPreviewShare)]
    public async Task ARememberedPreviewShareOutsideTheBoundsIsReadAtTheNearestBoundAsync(double written, double read)
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, LayoutFile.FileName), $$"""{ "sidebar": "shown", "previewShare": {{written.ToString(System.Globalization.CultureInfo.InvariantCulture)}} }""", Cancellation);

        await using var file = new LayoutFile(new AvalaPaths(data.Path));

        Assert.Equal(read, (await file.ReadAsync(Cancellation)).PreviewShare);
    }

    [Theory]
    [InlineData(120, WindowLayout.NarrowestSidebar)]
    [InlineData(900, WindowLayout.WidestSidebar)]
    public async Task ARememberedWidthOutsideTheBoundsIsReadAtTheNearestBoundAsync(double written, double read)
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, LayoutFile.FileName), $$"""{ "sidebar": "shown", "sidebarWidth": {{written}} }""", Cancellation);

        await using var file = new LayoutFile(new AvalaPaths(data.Path));

        Assert.Equal(read, (await file.ReadAsync(Cancellation)).SidebarWidth);
    }
}
