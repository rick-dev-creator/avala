using System.Text.Json;

if (args is not [var transcript])
{
    await Console.Error.WriteLineAsync("Usage: dotnet run scripts/session-cost.cs -- <claude-code-session.jsonl>");
    return 1;
}

var root = Repository.Root();
var pricing = await PricingFile.LoadAsync(Path.Combine(root.FullName, "docs", "sessions", "pricing.json"));
var session = await Transcripts.ReadAsync(transcript);

Console.WriteLine(CostReport.Of(session, pricing));

return 0;

internal static class Transcripts
{
    public static async Task<Session> ReadAsync(string transcript)
    {
        var subagents = Path.Combine(Path.ChangeExtension(transcript, null), "subagents");
        var files = Directory.Exists(subagents)
            ? [transcript, .. Directory.EnumerateFiles(subagents, "*.jsonl").Order(StringComparer.Ordinal)]
            : new[] { transcript };
        var responses = new Dictionary<string, Response>(StringComparer.Ordinal);
        var timestamps = new List<DateTimeOffset>();

        foreach (var file in files)
        {
            await foreach (var entry in EntriesAsync(file))
            {
                Record(entry, responses, timestamps);
            }
        }

        return new Session(files.Length, [.. responses.Values], timestamps.Min(), timestamps.Max());
    }

    private static void Record(Entry entry, Dictionary<string, Response> responses, List<DateTimeOffset> timestamps)
    {
        if (entry.Timestamp is { } timestamp)
        {
            timestamps.Add(timestamp);
        }

        if (entry.Response is { } response)
        {
            responses.TryAdd(response.Id, response);
        }
    }

    private static async IAsyncEnumerable<Entry> EntriesAsync(string file)
    {
        await foreach (var line in File.ReadLinesAsync(file))
        {
            if (Parse(line) is { } entry)
            {
                yield return entry;
            }
        }
    }

    private static Entry? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            DateTimeOffset? timestamp = entry.TryGetProperty("timestamp", out var time) && time.TryGetDateTimeOffset(out var parsed)
                ? parsed
                : null;

            return new Entry(timestamp, ReadResponse(entry));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Response? ReadResponse(JsonElement entry)
    {
        if (!entry.TryGetProperty("type", out var type) || type.GetString() != "assistant"
            || !entry.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("id", out var id))
        {
            return null;
        }

        var written = Count(usage, "cache_creation_input_tokens");
        var hasBreakdown = usage.TryGetProperty("cache_creation", out var creation) && creation.ValueKind == JsonValueKind.Object;
        var oneHour = hasBreakdown ? Count(creation, "ephemeral_1h_input_tokens") : 0;

        return new Response(
            id.GetString() ?? string.Empty,
            message.TryGetProperty("model", out var model) ? model.GetString() ?? "unknown" : "unknown",
            new Tokens(
                Count(usage, "input_tokens"),
                Count(usage, "output_tokens"),
                written - oneHour,
                oneHour,
                Count(usage, "cache_read_input_tokens")));
    }

    private static long Count(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;
}

internal static class PricingFile
{
    public static async Task<Pricing> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        return new Pricing(
            root.GetProperty("source").GetString() ?? string.Empty,
            root.GetProperty("currency").GetString() ?? string.Empty,
            root.GetProperty("models").EnumerateObject().ToDictionary(
                model => model.Name,
                model => new Price(
                    model.Value.GetProperty("input").GetDecimal(),
                    model.Value.GetProperty("output").GetDecimal(),
                    model.Value.GetProperty("cacheWrite5m").GetDecimal(),
                    model.Value.GetProperty("cacheWrite1h").GetDecimal(),
                    model.Value.GetProperty("cacheRead").GetDecimal()),
                StringComparer.Ordinal));
    }

    public static decimal Cost(Tokens tokens, Price price) =>
        (tokens.Input * price.Input
            + tokens.Output * price.Output
            + tokens.CacheWrite5m * price.CacheWrite5m
            + tokens.CacheWrite1h * price.CacheWrite1h
            + tokens.CacheRead * price.CacheRead) / 1_000_000m;
}

internal static class CostReport
{
    public static string Of(Session session, Pricing pricing)
    {
        var rows = session.Responses
            .GroupBy(response => response.Model, StringComparer.Ordinal)
            .Select(group => new ModelUsage(
                group.Key,
                group.Count(),
                new Tokens(
                    group.Sum(response => response.Tokens.Input),
                    group.Sum(response => response.Tokens.Output),
                    group.Sum(response => response.Tokens.CacheWrite5m),
                    group.Sum(response => response.Tokens.CacheWrite1h),
                    group.Sum(response => response.Tokens.CacheRead))))
            .OrderBy(usage => usage.Model, StringComparer.Ordinal)
            .Select(usage => (Usage: usage, Cost: pricing.Models.TryGetValue(usage.Model, out var price) ? PricingFile.Cost(usage.Tokens, price) : (decimal?)null))
            .ToList();
        var total = rows.Sum(row => row.Cost ?? 0m);

        return $"""
            | Measure | Value |
            | --- | ---: |
            | Started | {session.Started:yyyy-MM-dd HH:mm} UTC |
            | Ended | {session.Ended:yyyy-MM-dd HH:mm} UTC |
            | Duration | {(session.Ended - session.Started).TotalHours:0.0} h |
            | Model responses | {session.Responses.Count:N0} |
            | Transcripts read | {session.Transcripts} |

            | Model | Responses | Input | Output | Cache writes 5m | Cache writes 1h | Cache reads | Cost at API prices |
            | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
            {string.Join(Environment.NewLine, rows.Select(row => Row(row.Usage, row.Cost)))}

            Total at API list prices: **${total:N2} {pricing.Currency}**, using {pricing.Source.ToLowerInvariant()}.
            """;
    }

    private static string Row(ModelUsage usage, decimal? cost) =>
        $"| {usage.Model} | {usage.Responses:N0} | {usage.Tokens.Input:N0} | {usage.Tokens.Output:N0} | {usage.Tokens.CacheWrite5m:N0} | {usage.Tokens.CacheWrite1h:N0} | {usage.Tokens.CacheRead:N0} | {(cost is { } value ? $"${value:N2}" : "unpriced")} |";
}

internal static class Repository
{
    public static DirectoryInfo Root() => Find(new DirectoryInfo(Directory.GetCurrentDirectory()));

    private static DirectoryInfo Find(DirectoryInfo directory) =>
        File.Exists(Path.Combine(directory.FullName, "Avala.slnx"))
            ? directory
            : Find(directory.Parent ?? throw new InvalidOperationException("Run the script inside the repository."));
}

internal sealed record Entry(DateTimeOffset? Timestamp, Response? Response);

internal sealed record Response(string Id, string Model, Tokens Tokens);

internal sealed record Session(int Transcripts, IReadOnlyList<Response> Responses, DateTimeOffset Started, DateTimeOffset Ended);

internal sealed record Tokens(long Input, long Output, long CacheWrite5m, long CacheWrite1h, long CacheRead);

internal sealed record ModelUsage(string Model, int Responses, Tokens Tokens);

internal sealed record Price(decimal Input, decimal Output, decimal CacheWrite5m, decimal CacheWrite1h, decimal CacheRead);

internal sealed record Pricing(string Source, string Currency, Dictionary<string, Price> Models);
