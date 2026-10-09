using Avala.Recording.Recordings;
using Avala.Recording.Settings;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Recording.Tests.Settings;

public sealed class SettingsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("{}", false, "")]
    [InlineData("""{ "enabled": true }""", true, "")]
    [InlineData("""{ "enabled": true, "redact": ["ana@example.com", "sk-123"] }""", true, "ana@example.com|sk-123")]
    public void AValidFileTurnsRecordingOnWithItsRedactions(string text, bool enabled, string redactions)
    {
        var settings = Outcomes.Succeeds(SettingsParser.Parse(text));

        Assert.Equal((enabled, redactions), (settings.Enabled, string.Join('|', settings.Redactions)));
    }

    [Theory]
    [InlineData("not json", nameof(RecordingError.Malformed))]
    [InlineData("[]", nameof(RecordingError.Malformed))]
    [InlineData("""{ "enabled": "yes" }""", nameof(RecordingError.Malformed))]
    [InlineData("""{ "enabled": true, "enabled": false }""", nameof(RecordingError.Malformed))]
    [InlineData("""{ "redact": "sk-123" }""", nameof(RecordingError.Malformed))]
    [InlineData("""{ "redact": [1] }""", nameof(RecordingError.Malformed))]
    [InlineData("""{ "redact": [" "] }""", nameof(RecordingError.InvalidRedaction))]
    [InlineData("""{ "record": true }""", nameof(RecordingError.UnknownField))]
    public void AnInvalidFileIsRejectedWithItsReason(string text, string expected) =>
        Assert.Equal(Enum.Parse<RecordingError>(expected), Outcomes.FailsWith(SettingsParser.Parse(text)));

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "enabled": true, "verbose": true }""")]
    public async Task WithoutAValidFileSessionsAreNotRecordedAsync(string? text)
    {
        using var data = new TemporaryFolder();

        if (text is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(data.Path, SettingsFile.FileName), text, Cancellation);
        }

        Assert.Equal(RecordingSettings.Off, await new SettingsFile(new AvalaPaths(data.Path), NullLogger<SettingsFile>.Instance).LoadAsync(Cancellation));
    }

    [Fact]
    public async Task TheFileOfTheDataFolderTurnsRecordingOnAsync()
    {
        using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, SettingsFile.FileName), """{ "enabled": true }""", Cancellation);

        Assert.True((await new SettingsFile(new AvalaPaths(data.Path), NullLogger<SettingsFile>.Instance).LoadAsync(Cancellation)).Enabled);
    }
}
