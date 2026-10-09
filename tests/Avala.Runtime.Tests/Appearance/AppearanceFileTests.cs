using Avala.Runtime.Appearance;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Testing;

namespace Avala.Runtime.Tests.Appearance;

public sealed class AppearanceFileTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAFileTheOperatingSystemsThemeAndFullMotionApplyAsync()
    {
        await using var data = new TemporaryFolder();
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());

        var settings = await file.ReadAsync(Cancellation);

        Assert.Equal(new AppearanceSettings(new AppearancePreference(ThemeChoice.System, false), AppearanceFileStatus.Absent, Option<AppearanceError>.None), settings);
    }

    [Fact]
    public async Task AChosenAppearanceIsWrittenPublishedAndReadBackByTheNextStartAsync()
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), bus);

        var changed = await file.ChangeAsync(new AppearancePreference(ThemeChoice.Light, true), Cancellation);
        await using var restarted = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());
        var read = await restarted.ReadAsync(Cancellation);

        Assert.True(changed.IsSuccess);
        Assert.Equal([new AppearanceChanged(new AppearancePreference(ThemeChoice.Light, true))], bus.Published);
        Assert.Equal(new AppearanceSettings(new AppearancePreference(ThemeChoice.Light, true), AppearanceFileStatus.Applied, Option<AppearanceError>.None), read);
        Assert.Equal("{\n  \"theme\": \"light\",\n  \"reduceMotion\": true\n}\n", (await File.ReadAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), Cancellation)).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task StartingPublishesTheAppearanceReadFromTheFileAsync()
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), """{ "theme": "dark" }""", Cancellation);
        var bus = new RecordingBus();
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), bus);

        await file.RunAsync(Cancellation);

        Assert.Equal([new AppearanceChanged(new AppearancePreference(ThemeChoice.Dark, false))], bus.Published);
    }

    [Theory]
    [InlineData("""{ "theme": "sepia" }""")]
    [InlineData("""{ "theme": "1" }""")]
    [InlineData("""{ "theme": 2 }""")]
    [InlineData("""{ "reduceMotion": "yes" }""")]
    [InlineData("""[ "light" ]""")]
    [InlineData("not json")]
    public async Task AFileThatIsNotAnAppearanceIsRejectedAndTheDefaultsApplyAsync(string content)
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), content, Cancellation);
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());

        var settings = await file.ReadAsync(Cancellation);

        Assert.Equal(new AppearanceSettings(AppearancePreference.Default, AppearanceFileStatus.Rejected, AppearanceError.Invalid), settings);
    }

    [Fact]
    public async Task AFileTooLargeToBeAnAppearanceIsRejectedAsync()
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), new string(' ', AppearanceFile.MaximumBytes + 1), Cancellation);
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());

        Assert.Equal(Option<AppearanceError>.Some(AppearanceError.TooLarge), (await file.ReadAsync(Cancellation)).Error);
    }
}
