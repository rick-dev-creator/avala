using System.Collections.Immutable;
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

    private static readonly Dictionary<string, Func<JsonElement, bool>> Shape = new(StringComparer.Ordinal)
    {
        ["child"] = IsText,
        ["request"] = IsText,
        ["decision"] = IsText,
        ["message"] = IsText,
        ["fields"] = fields => fields.ValueKind == JsonValueKind.Array,
    };

    private static readonly Dictionary<string, Func<JsonElement, bool>> FieldShape = new(StringComparer.Ordinal)
    {
        ["id"] = IsText,
        ["chosen"] = chosen => chosen.ValueKind == JsonValueKind.Array && chosen.EnumerateArray().All(IsText),
        ["text"] = IsText,
        ["confirmed"] = confirmed => confirmed.ValueKind is JsonValueKind.True or JsonValueKind.False,
    };

    public FormAnswer FormAnswer =>
        Decision == ChildDecision.Deny
            ? new FormAnswer(Request, []) { Declined = true, Message = Message }
            : new FormAnswer(Request, Fields) { Message = Message };

    public static Option<AnswerChildInput> Parse(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input, Options);

            return Fits(document.RootElement, Shape) ? Read(document.RootElement) : Option<AnswerChildInput>.None;
        }
        catch (JsonException)
        {
            return Option<AnswerChildInput>.None;
        }
    }

    private static Option<AnswerChildInput> Read(JsonElement root)
    {
        var child = Text(root, "child").Bind(text => Guid.TryParse(text, out var job) ? new JobId(job) : Option<JobId>.None);
        var request = Text(root, "request").Bind(text => text.Length > 0 ? new ItemId(text) : Option<ItemId>.None);
        var decision = Text(root, "decision").Bind(DecisionIn);
        var said = Text(root, "message").Bind(text => string.IsNullOrWhiteSpace(text) ? Option<string>.None : text);
        var fields = root.TryGetProperty("fields", out var listed) ? FieldsIn(listed) : Option<IReadOnlyList<FieldAnswer>>.Some([]);

        return child.Bind(job => request.Bind(item => decision.Bind(chosen => fields.Map(answers =>
            new AnswerChildInput(job, item, chosen, said, answers)))));
    }

    private static bool IsText(JsonElement value) => value.ValueKind == JsonValueKind.String;

    private static bool Fits(JsonElement element, Dictionary<string, Func<JsonElement, bool>> shape) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().All(property => shape.TryGetValue(property.Name, out var fits) && fits(property.Value));

    private static Option<string> Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : Option<string>.None;

    private static Option<ChildDecision> DecisionIn(string decision) => decision switch
    {
        "allow" => ChildDecision.Allow,
        "deny" => ChildDecision.Deny,
        "person" => ChildDecision.Person,
        _ => Option<ChildDecision>.None,
    };

    private static Option<IReadOnlyList<FieldAnswer>> FieldsIn(JsonElement fields) =>
        fields.EnumerateArray()
            .Aggregate(Option<ImmutableList<FieldAnswer>>.Some([]), (answers, field) => answers.Bind(kept => FieldIn(field).Map(kept.Add)))
            .Map(answers => (IReadOnlyList<FieldAnswer>)answers);

    private static Option<FieldAnswer> FieldIn(JsonElement field) =>
        Fits(field, FieldShape)
            ? Text(field, "id").Bind(id => id.Length == 0 ? Option<FieldAnswer>.None : new FieldAnswer(id)
            {
                Chosen = field.TryGetProperty("chosen", out var chosen) ? [.. chosen.EnumerateArray().Select(label => label.GetString() ?? string.Empty)] : [],
                Text = Text(field, "text"),
                Confirmed = field.TryGetProperty("confirmed", out var confirmed) && confirmed.ValueKind == JsonValueKind.True,
            })
            : Option<FieldAnswer>.None;
}
