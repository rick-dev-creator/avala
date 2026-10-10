using System.Text.Json.Nodes;
using Avala.ClaudeCode.Cli;
using Avala.ClaudeCode.Conversations;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.ClaudeCode.Tests.Cli;

public sealed class TranscriptTapTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATranscriptStartsWithTheLaunchThenKeepsEveryLineUnderItsDirectionAsJsonOrTextAsync()
    {
        await using var folder = new TemporaryFolder();
        var transcripts = Path.Combine(folder.Path, "transcripts");
        var launch = new CliLaunch(
            folder.Path,
            ["--print", "--resume", "abc-123"],
            [],
            new Dictionary<string, string> { [CommandLine.ConfigurationVariable] = Path.Combine(folder.Path, ".claude-work") + Path.DirectorySeparatorChar, [CommandLine.TodoToolsVariable] = "1" });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 30, 0, 123, TimeSpan.Zero));

        await using (var tap = TranscriptTap.Open(transcripts, launch, clock))
        {
            tap.Note(TranscriptTap.Input, """{ "type": "user" }""");
            tap.Note(TranscriptTap.Output, "not json");
        }

        var file = Assert.Single(Directory.GetFiles(transcripts));
        var lines = (await File.ReadAllLinesAsync(file, Cancellation)).Select(line => JsonNode.Parse(line)!).ToList();
        Assert.StartsWith("20261010T093000123Z-", Path.GetFileName(file), StringComparison.Ordinal);
        Assert.Equal(
            ("abc-123", ".claude-work", "1", 3, "user", "not json"),
            ((string?)lines[0]["resume"], (string?)lines[0]["folder"], (string?)lines[0]["todoTools"], lines[0]["arguments"]!.AsArray().Count, (string?)lines[1]["in"]!["type"], (string?)lines[2]["out"]));
    }
}
