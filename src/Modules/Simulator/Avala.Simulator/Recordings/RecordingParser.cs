using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Recordings;

internal sealed class RecordingParser
{
    public const string Format = "avala-recording";

    public const int Version = 1;

    public const string WorkingDirectoryMark = "${workingDirectory}";

    private const string StreamFailed = "The recorded agent's event stream failed.";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 32, AllowDuplicateProperties = false };

    private readonly string workingDirectory;

    private RecordingParser(string workingDirectory) => this.workingDirectory = workingDirectory;

    public static Result<RecordedSession, ReplayError> Parse(string text, string workingDirectory)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);
            var root = document.RootElement;

            return root.GetProperty("format").GetString() != Format ? ReplayError.Malformed
                : root.GetProperty("version").GetInt32() != Version ? ReplayError.UnsupportedVersion
                : new RecordingParser(workingDirectory).Session(root);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            return ReplayError.Malformed;
        }
    }

    private static TEnum Enum<TEnum>(JsonElement element)
        where TEnum : struct, System.Enum =>
        element.GetString() is { Length: > 0 } text && char.IsLetter(text[0]) && System.Enum.TryParse<TEnum>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(value)
            ? value
            : throw new FormatException($"{element.GetRawText()} is not a {typeof(TEnum).Name}.");

    private static ItemId Item(JsonElement element) => new(element.GetProperty("item").GetString() ?? throw new FormatException("An item has no identifier."));

    private static Option<JsonElement> Property(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : Option<JsonElement>.None;

    private RecordedSession Session(JsonElement root)
    {
        var steps = new List<IStep>();
        var previous = 0L;

        foreach (var entry in root.GetProperty("entries").EnumerateArray())
        {
            var at = entry.GetProperty("at").GetInt64();
            var gap = TimeSpan.FromMilliseconds(Math.Max(0, at - previous));
            previous = at;

            if (!entry.TryGetProperty("refused", out _))
            {
                steps.AddRange(Step(entry, gap));
            }
        }

        return new RecordedSession(Enum<PermissionMode>(root.GetProperty("options").GetProperty("permissions")), steps);
    }

    private IEnumerable<IStep> Step(JsonElement entry, TimeSpan gap) =>
        Property(entry, "event").Match<IEnumerable<IStep>>(
            recorded => [new Emit(gap, Event(recorded))],
            () => Property(entry, "file").Match<IEnumerable<IStep>>(
                file => Property(file, "content").Match<IEnumerable<IStep>>(
                    content => [new PutFile(gap, Text(file.GetProperty("path")), Text(content))],
                    () => []),
                () => Property(entry, "respond").Match<IEnumerable<IStep>>(
                    respond => [new AwaitPermission(new PermissionDecision(Item(respond), Enum<PermissionAnswer>(respond.GetProperty("answer"))) { Message = Optional(respond, "message") })],
                    () => Property(entry, "answer").Match<IEnumerable<IStep>>(
                        answer => [new AwaitAnswer(Answer(answer))],
                        () => entry.TryGetProperty("interrupt", out _) ? [new AwaitInterrupt()]
                            : Property(entry, "end").Match<IEnumerable<IStep>>(
                                end => [end.GetProperty("crashed").GetBoolean() ? new Crash(StreamFailed) : new Hangup()],
                                () => [])))));

    private IAgentEvent Event(JsonElement recorded) => recorded.GetProperty("type").GetString() switch
    {
        "turnStarted" => new TurnStarted(default, default),
        "itemStarted" => new ItemStarted(default, default, Item(recorded), Enum<ItemKind>(recorded.GetProperty("kind")), Text(recorded.GetProperty("title"))),
        "canvasStarted" => new CanvasStarted(default, default, Item(recorded), Text(recorded.GetProperty("title")), Plain(recorded.GetProperty("mediaType"))),
        "itemProgressed" => new ItemProgressed(default, default, Item(recorded), Text(recorded.GetProperty("text"))),
        "itemCompleted" => new ItemCompleted(default, default, Item(recorded), Enum<ItemOutcome>(recorded.GetProperty("outcome"))),
        "permissionRequested" => new PermissionRequested(
            default,
            default,
            Item(recorded),
            Text(recorded.GetProperty("title")),
            Enum<ItemKind>(recorded.GetProperty("kind")),
            Text(recorded.GetProperty("target"))),
        "permissionResolved" => new PermissionResolved(default, default, Item(recorded), Enum<PermissionAnswer>(recorded.GetProperty("answer"))),
        "formRequested" => new FormRequested(default, default, Item(recorded), Form(recorded.GetProperty("form"))),
        "formAnswered" => new FormAnswered(default, default, Item(recorded), Answer(recorded.GetProperty("answer"))),
        "planUpdated" => new PlanUpdated(default, default, [.. recorded.GetProperty("steps").EnumerateArray().Select(step =>
            new PlanStep(Text(step.GetProperty("title")), Enum<PlanStepStatus>(step.GetProperty("status"))))]),
        "usageReported" => Usage(recorded),
        "limitReported" => new LimitReported(default, default, new UsageLimit(
            Plain(recorded.GetProperty("limit").GetProperty("window")),
            recorded.GetProperty("limit").GetProperty("usedFraction").GetDouble(),
            Property(recorded.GetProperty("limit"), "resetsAt").Map(resets => resets.GetDateTimeOffset()))),
        "resumeTokenIssued" => new ResumeTokenIssued(default, default, new ResumeToken(Text(recorded.GetProperty("token")))),
        "turnCompleted" => new TurnCompleted(default, default, Enum<TurnOutcome>(recorded.GetProperty("outcome"))),
        var unknown => throw new FormatException($"{unknown} is not an agent event."),
    };

    private static UsageReported Usage(JsonElement recorded)
    {
        var tokens = recorded.GetProperty("tokens");

        return new UsageReported(
            default,
            default,
            new TokenUsage(
                tokens.GetProperty("input").GetInt64(),
                tokens.GetProperty("output").GetInt64(),
                tokens.GetProperty("cacheRead").GetInt64(),
                tokens.GetProperty("cacheWrite").GetInt64(),
                tokens.GetProperty("reasoning").GetInt64()),
            Property(recorded, "cost").Map(cost => new Cost(cost.GetProperty("amount").GetDecimal(), Plain(cost.GetProperty("currency")))));
    }

    private AgentForm Form(JsonElement form) =>
        new(
            Enum<FormPurpose>(form.GetProperty("purpose")),
            Text(form.GetProperty("title")),
            Text(form.GetProperty("context")),
            [.. form.GetProperty("fields").EnumerateArray().Select(field => new FormField(
                Plain(field.GetProperty("id")),
                Text(field.GetProperty("header")),
                Text(field.GetProperty("prompt")),
                Enum<FieldKind>(field.GetProperty("kind")),
                [.. field.GetProperty("options").EnumerateArray().Select(option => new FormOption(
                    Text(option.GetProperty("label")),
                    Text(option.GetProperty("description")),
                    option.GetProperty("recommended").GetBoolean()))],
                field.GetProperty("acceptsFreeText").GetBoolean()))]);

    private FormAnswer Answer(JsonElement answer) =>
        new(Item(answer), [.. answer.GetProperty("fields").EnumerateArray().Select(field => new FieldAnswer(Plain(field.GetProperty("field")))
        {
            Chosen = [.. field.GetProperty("chosen").EnumerateArray().Select(Text)],
            Text = Optional(field, "text"),
            Confirmed = field.GetProperty("confirmed").GetBoolean(),
        })])
        {
            Declined = answer.GetProperty("declined").GetBoolean(),
            Message = Optional(answer, "message"),
        };

    private Option<string> Optional(JsonElement element, string name) => Property(element, name).Map(Text);

    private static string Plain(JsonElement element) => element.GetString() ?? throw new FormatException("A text is missing.");

    private string Text(JsonElement element) => Plain(element).Replace(WorkingDirectoryMark, workingDirectory, StringComparison.Ordinal);
}
