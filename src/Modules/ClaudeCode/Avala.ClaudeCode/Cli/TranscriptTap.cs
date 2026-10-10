using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avala.ClaudeCode.Conversations;

namespace Avala.ClaudeCode.Cli;

internal sealed class TranscriptTap : IAsyncDisposable
{
    public const string Input = "in";

    public const string Output = "out";

    private readonly Channel<string> entries = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task writing;

    private TranscriptTap(string path) => writing = Task.Run(() => WriteAsync(path));

    public static TranscriptTap Open(string folder, CliLaunch launch, TimeProvider clock)
    {
        Directory.CreateDirectory(folder);
        var name = $"{clock.GetUtcNow().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.jsonl";
        var tap = new TranscriptTap(Path.Combine(folder, name));
        var resume = launch.Arguments.SkipWhile(argument => argument != "--resume").Skip(1).FirstOrDefault();
        var folderName = launch.Variables.TryGetValue(CommandLine.ConfigurationVariable, out var configuration)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(configuration))
            : null;
        tap.entries.Writer.TryWrite(new JsonObject
        {
            ["transcript"] = 1,
            ["resume"] = resume,
            ["folder"] = folderName,
            ["todoTools"] = launch.Variables.TryGetValue(CommandLine.TodoToolsVariable, out var todoTools) ? todoTools : null,
            ["arguments"] = new JsonArray([.. launch.Arguments.Select(argument => JsonValue.Create(argument))]),
        }.ToJsonString());

        return tap;
    }

    public void Note(string direction, string line) =>
        entries.Writer.TryWrite(new JsonObject { [direction] = Parsed(line) }.ToJsonString());

    public async ValueTask DisposeAsync()
    {
        entries.Writer.TryComplete();
        await writing;
    }

    private static JsonNode? Parsed(string line)
    {
        try
        {
            return JsonNode.Parse(line) ?? JsonValue.Create(line);
        }
        catch (JsonException)
        {
            return JsonValue.Create(line);
        }
    }

    private async Task WriteAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await foreach (var entry in entries.Reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(entry);
            await writer.FlushAsync();
        }
    }
}
