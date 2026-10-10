using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Escalating;

internal enum ChildDecision
{
    Allow,
    Deny,
    Person,
}

internal sealed record AnswerChildInput(JobId Child, ItemId Request, ChildDecision Decision, Option<string> Message, IReadOnlyList<FieldAnswer> Fields)
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 5, AllowDuplicateProperties = false };

    private static readonly string[] Known = ["child", "request", "decision", "message", "fields"];

    private static readonly string[] FieldKeys = ["id", "chosen", "text", "confirmed"];

    public FormAnswer FormAnswer =>
        Decision == ChildDecision.Deny
            ? new FormAnswer(Request, []) { Declined = true, Message = Message }
            : new FormAnswer(Request, Fields) { Message = Message };

    public static Option<AnswerChildInput> Parse(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input, Options);

            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return Option<AnswerChildInput>.None;
        }
    }

    private static Option<AnswerChildInput> Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.EnumerateObject().All(field => Known.Contains(field.Name, StringComparer.Ordinal))
            || (root.TryGetProperty("message", out var message) && message.ValueKind != JsonValueKind.String))
        {
            return Option<AnswerChildInput>.None;
        }

        var child = Text(root, "child").Bind(text => Guid.TryParse(text, out var job) ? new JobId(job) : Option<JobId>.None);
        var request = Text(root, "request").Bind(text => text.Length > 0 ? new ItemId(text) : Option<ItemId>.None);
        var decision = Text(root, "decision").Bind(DecisionIn);
        var said = Text(root, "message").Bind(text => string.IsNullOrWhiteSpace(text) ? Option<string>.None : text);

        return child.Bind(job => request.Bind(item => decision.Bind(chosen => FieldsIn(root).Map(fields =>
            new AnswerChildInput(job, item, chosen, said, fields)))));
    }

    private static Option<string> Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : Option<string>.None;

    private static Option<ChildDecision> DecisionIn(string decision) => decision switch
    {
        "allow" => ChildDecision.Allow,
        "deny" => ChildDecision.Deny,
        "person" => ChildDecision.Person,
        _ => Option<ChildDecision>.None,
    };

    private static Option<IReadOnlyList<FieldAnswer>> FieldsIn(JsonElement root)
    {
        if (!root.TryGetProperty("fields", out var fields))
        {
            return Option<IReadOnlyList<FieldAnswer>>.Some([]);
        }

        if (fields.ValueKind != JsonValueKind.Array)
        {
            return Option<IReadOnlyList<FieldAnswer>>.None;
        }

        var answers = fields.EnumerateArray().Select(FieldIn).ToList();

        return answers.All(answer => answer.IsSome)
            ? Option<IReadOnlyList<FieldAnswer>>.Some([.. answers.Select(answer => answer.Match(found => found, () => new FieldAnswer(string.Empty)))])
            : Option<IReadOnlyList<FieldAnswer>>.None;
    }

    private static Option<FieldAnswer> FieldIn(JsonElement field)
    {
        if (field.ValueKind != JsonValueKind.Object || !field.EnumerateObject().All(key => FieldKeys.Contains(key.Name, StringComparer.Ordinal)))
        {
            return Option<FieldAnswer>.None;
        }

        var hasChosen = field.TryGetProperty("chosen", out var chosen);

        if ((hasChosen && (chosen.ValueKind != JsonValueKind.Array || chosen.EnumerateArray().Any(label => label.ValueKind != JsonValueKind.String)))
            || (field.TryGetProperty("text", out var text) && text.ValueKind != JsonValueKind.String)
            || (field.TryGetProperty("confirmed", out var confirmed) && confirmed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
        {
            return Option<FieldAnswer>.None;
        }

        return Text(field, "id").Bind(id => id.Length == 0 ? Option<FieldAnswer>.None : new FieldAnswer(id)
        {
            Chosen = hasChosen ? [.. chosen.EnumerateArray().Select(label => label.GetString() ?? string.Empty)] : [],
            Text = Text(field, "text"),
            Confirmed = field.TryGetProperty("confirmed", out var yes) && yes.ValueKind == JsonValueKind.True,
        });
    }
}
