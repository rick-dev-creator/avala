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

        Assert.Equal(new AppearanceSettings(new AppearancePreference(ThemeChoice.System, MotionChoice.System), AppearanceFileStatus.Absent, Option<AppearanceError>.None), settings);
    }

    [Fact]
    public async Task AChosenAppearanceIsWrittenPublishedAndReadBackByTheNextStartAsync()
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), bus);

        var chosen = new AppearancePreference(ThemeChoice.Light, MotionChoice.Reduced) { Send = SendKey.CommandEnter, HidesPersonalDetails = false };
        var changed = await file.ChangeAsync(chosen, Cancellation);
        await using var restarted = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());
        var read = await restarted.ReadAsync(Cancellation);

        Assert.True(changed.IsSuccess);
        Assert.Equal([new AppearanceChanged(chosen)], bus.Published);
        Assert.Equal(new AppearanceSettings(chosen, AppearanceFileStatus.Applied, Option<AppearanceError>.None), read);
        Assert.Equal("{\n  \"theme\": \"light\",\n  \"reduceMotion\": \"on\",\n  \"readingWidth\": \"comfortable\",\n  \"send\": \"commandEnter\",\n  \"hidePersonalDetails\": false\n}\n", (await File.ReadAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), Cancellation)).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(ThemeChoice.Tide, "tide")]
    [InlineData(ThemeChoice.Unit, "unit")]
    [InlineData(ThemeChoice.Ash, "ash")]
    [InlineData(ThemeChoice.Bloom, "bloom")]
    [InlineData(ThemeChoice.Signal, "signal")]
    public async Task ANamedThemeIsWrittenByItsNameAndReadBackAsync(ThemeChoice theme, string name)
    {
        await using var data = new TemporaryFolder();
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());

        await file.ChangeAsync(new AppearancePreference(theme, MotionChoice.System), Cancellation);
        await using var restarted = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());

        Assert.Contains($"\"theme\": \"{name}\"", await File.ReadAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), Cancellation), StringComparison.Ordinal);
        Assert.Equal(theme, (await restarted.ReadAsync(Cancellation)).Preference.Theme);
    }

    [Fact]
    public async Task StartingPublishesTheAppearanceReadFromTheFileAsync()
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), """{ "theme": "dark" }""", Cancellation);
        var bus = new RecordingBus();
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), bus);

        await file.RunAsync(Cancellation);

        Assert.Equal([new AppearanceChanged(new AppearancePreference(ThemeChoice.Dark, MotionChoice.System))], bus.Published);
    }

    [Theory]
    [InlineData("""{ "reduceMotion": "system" }""", MotionChoice.System)]
    [InlineData("""{ "reduceMotion": "on" }""", MotionChoice.Reduced)]
    [InlineData("""{ "reduceMotion": "off" }""", MotionChoice.Full)]
    [InlineData("""{ "reduceMotion": true }""", MotionChoice.Reduced)]
    [InlineData("""{ "reduceMotion": false }""", MotionChoice.Full)]
    [InlineData("""{ }""", MotionChoice.System)]
    public async Task ReducedMotionIsReadAsSystemOnOrOffAsync(string content, MotionChoice motion)
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), content, Cancellation);
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());

        Assert.Equal(motion, (await file.ReadAsync(Cancellation)).Preference.Motion);
    }

    [Theory]
    [InlineData("""{ "readingWidth": "comfortable" }""", ReadingChoice.Comfortable)]
    [InlineData("""{ "readingWidth": "wide" }""", ReadingChoice.Wide)]
    [InlineData("""{ }""", ReadingChoice.Comfortable)]
    public async Task TheReadingWidthIsReadAsComfortableOrWideAsync(string content, ReadingChoice reading)
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), content, Cancellation);
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());

        Assert.Equal(reading, (await file.ReadAsync(Cancellation)).Preference.Reading);
    }

    [Theory]
    [InlineData("""{ "hidePersonalDetails": false }""", false)]
    [InlineData("""{ "hidePersonalDetails": true }""", true)]
    [InlineData("""{ }""", true)]
    public async Task PersonalDetailsStayHiddenUnlessTheFileRevealsThemAsync(string content, bool hidden)
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, AppearanceFile.FileName), content, Cancellation);
        await using var file = new AppearanceFile(new AvalaPaths(data.Path), new RecordingBus());

        Assert.Equal(hidden, (await file.ReadAsync(Cancellation)).Preference.HidesPersonalDetails);
    }

    [Theory]
    [InlineData("""{ "hidePersonalDetails": "no" }""")]
    [InlineData("""{ "readingWidth": "huge" }""")]
    [InlineData("""{ "theme": "sepia" }""")]
    [InlineData("""{ "theme": "1" }""")]
    [InlineData("""{ "theme": 2 }""")]
    [InlineData("""{ "reduceMotion": "yes" }""")]
    [InlineData("""{ "send": "space" }""")]
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
