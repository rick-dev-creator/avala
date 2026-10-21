using Avala.ClaudeCode.Replay;
using Avala.Testing;

namespace Avala.Agents.Tests.Conformance;

public sealed class TranscriptPlayerTests
{
    private const string Transcript = """
        {"transcript":1,"resume":null,"folder":null,"arguments":[]}
        {"in":{"type":"control_request","request_id":"init","request":{"subtype":"initialize"}}}
        {"in":{"type":"user","message":{"content":"go"}}}
        {"out":{"type":"system","subtype":"init"}}
        {"in":{"type":"control_response","response":{"request_id":"r1"}}}
        {"out":{"type":"result","subtype":"success"}}
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InputsAreMatchedInAnyOrderWithinTheirGroupBeforeTheNextOutputsArePlayedAsync()
    {
        var (code, output, error) = await PlayAsync([], """{"type":"user"}""", """{"type":"control_request","request":{"subtype":"initialize"}}""", """{"type":"control_response","response":{"request_id":"r1"}}""");

        Assert.Equal((0, 2, string.Empty), (code, output.Length, error));
    }

    [Fact]
    public async Task AnInputTheTranscriptDoesNotExpectStopsTheReplayAndNamesWhatWasExpectedAsync()
    {
        var (code, output, error) = await PlayAsync([], """{"type":"user"}""", """{"type":"interrupt"}""");

        Assert.Equal((3, 0), (code, output.Length));
        Assert.Equal("Replay diverged: expected one of [request initialize] but received interrupt.", error);
    }

    [Fact]
    public async Task AnInputThatEndsEarlyEndsTheReplayQuietlyAsync()
    {
        var (code, output, error) = await PlayAsync([], """{"type":"user"}""");

        Assert.Equal((0, 0, string.Empty), (code, output.Length, error));
    }

    [Fact]
    public async Task ResumingASessionNoTranscriptRecordedFailsAsTheCliDoesAsync()
    {
        var (code, output, error) = await PlayAsync(["--resume", "abc"]);

        Assert.Equal((1, 0, "No conversation found with session ID: abc"), (code, output.Length, error));
    }

    [Fact]
    public async Task ARecordedTranscriptNamesAvalasOwnCommandsByTheirPlaceholderSoTheReplayKnowsThemAsAvalasAsync()
    {
        var foreign = new List<string>();

        foreach (var path in Directory.EnumerateFiles(Path.Combine(Repository.Root.FullName, "tests", "transcripts", "claude-code"), "*.jsonl", SearchOption.AllDirectories))
        {
            var lines = (await File.ReadAllLinesAsync(path, Cancellation)).Where(line => line.Length > 0).Select(line => System.Text.Json.Nodes.JsonNode.Parse(line)!).ToList();

            if (lines[0]["crafted"] is not null)
            {
                continue;
            }

            var sent = lines.Select(line => line["in"]).Where(line => (string?)line?["type"] == "user").Select(line => (string?)line?["uuid"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
            foreign.AddRange(lines
                .Select(line => line["out"])
                .Where(line => (string?)line?["type"] == "command_lifecycle" && sent.Contains((string?)line?["command_uuid"] ?? string.Empty) && !((string?)line?["command_uuid"] ?? string.Empty).StartsWith("${", StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}: {(string?)line?["command_uuid"]}"));
        }

        Assert.Empty(foreign.Distinct());
    }

    [Fact]
    public void APlaceWithBackslashesFilledIntoAJsonTextNestedInALineKeepsThatTextReadableAsJson()
    {
        var line = System.Text.Json.Nodes.JsonNode.Parse("""{"text":"{\"behavior\":\"allow\",\"file_path\":\"${configurationDirectory}/notes.txt\"}"}""")!;

        var placed = TranscriptPlayer.Placed(line, new Dictionary<string, string>(StringComparer.Ordinal) { [TranscriptPlayer.ConfigurationDirectory] = @"C:\Users\runner" });

        Assert.Equal(@"C:\Users\runner/notes.txt", (string?)System.Text.Json.Nodes.JsonNode.Parse((string)placed["text"]!)!["file_path"]);
    }

    private static async Task<(int Code, string[] Output, string Error)> PlayAsync(string[] arguments, params string[] inputs)
    {
        await using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "session.jsonl"), Transcript, Cancellation);
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await TranscriptPlayer.PlayAsync([folder.Path, .. arguments], new StringReader(string.Join('\n', inputs)), output, error);

        return (code, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries), error.ToString().Trim());
    }
}
