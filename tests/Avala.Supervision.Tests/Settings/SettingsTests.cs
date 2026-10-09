using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Supervision.Settings;
using Avala.Supervision.Watching;
using Avala.Testing;

namespace Avala.Supervision.Tests.Settings;

public sealed class SettingsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("{}", 900)]
    [InlineData("""{ "silenceSeconds": 90 }""", 90)]
    [InlineData("""{ "silenceSeconds": 0.5 }""", 0.5)]
    public void AValidFileSetsTheSilenceWindowOrKeepsTheDefault(string text, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Outcomes.Succeeds(SettingsParser.Parse(text)));

    [Theory]
    [InlineData("not json", SupervisionError.Malformed)]
    [InlineData("[]", SupervisionError.Malformed)]
    [InlineData("""{ "silenceSeconds": "90" }""", SupervisionError.Malformed)]
    [InlineData("""{ "silenceSeconds": 1, "silenceSeconds": 2 }""", SupervisionError.Malformed)]
    [InlineData("""{ "silence": 90 }""", SupervisionError.UnknownField)]
    [InlineData("""{ "silenceSeconds": 0 }""", SupervisionError.InvalidSilence)]
    [InlineData("""{ "silenceSeconds": 86401 }""", SupervisionError.InvalidSilence)]
    public void AnInvalidFileIsRejectedWithItsReason(string text, SupervisionError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(SettingsParser.Parse(text)));

    [Fact]
    public async Task WithoutASettingsFileTheDefaultWindowAppliesAsync()
    {
        using var data = new TemporaryFolder();

        Assert.Equal(
            new SupervisionSettings(JobWatch.DefaultSilence, SettingsFileStatus.Absent, Option<SupervisionError>.None),
            await new SettingsFile(new AvalaPaths(data.Path)).LoadAsync(Cancellation));
    }

    [Fact]
    public async Task TheSettingsFileOfTheDataFolderSetsTheWindowAsync()
    {
        using var data = new TemporaryFolder();
        await WriteAsync(data, """{ "silenceSeconds": 30 }""");

        Assert.Equal(
            new SupervisionSettings(TimeSpan.FromSeconds(30), SettingsFileStatus.Applied, Option<SupervisionError>.None),
            await new SettingsFile(new AvalaPaths(data.Path)).LoadAsync(Cancellation));
    }

    [Theory]
    [InlineData(false, SupervisionError.UnknownField)]
    [InlineData(true, SupervisionError.TooLarge)]
    public async Task AnInvalidSettingsFileKeepsTheDefaultWindowAndSaysWhyAsync(bool oversized, SupervisionError expected)
    {
        using var data = new TemporaryFolder();
        await WriteAsync(data, oversized ? new string(' ', SettingsFile.MaximumBytes + 1) : """{ "window": 30 }""");

        Assert.Equal(
            new SupervisionSettings(JobWatch.DefaultSilence, SettingsFileStatus.Rejected, expected),
            await new SettingsFile(new AvalaPaths(data.Path)).LoadAsync(Cancellation));
    }

    private static Task WriteAsync(TemporaryFolder data, string content) =>
        File.WriteAllTextAsync(Path.Combine(data.Path, SettingsFile.FileName), content, Cancellation);
}
