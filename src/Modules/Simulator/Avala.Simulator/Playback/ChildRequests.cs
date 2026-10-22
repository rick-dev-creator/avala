using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed record EarlyResult(Option<string> Answer, string Wait);

internal static class ChildRequests
{
    private const string FieldsLine = "fields ";

    public static Option<string> Answer(string message, string decision)
    {
        var lines = message.Split('\n').Select(line => line.Trim()).ToList();
        var fields = lines.Where(line => line.StartsWith(FieldsLine, StringComparison.Ordinal)).Select(line => Parsed(line[FieldsLine.Length..]).Bind(Array)).FirstOrDefault(found => found.IsSome);

        return lines
            .Where(line => line.StartsWith(ScenarioCatalog.AnswerChild + " ", StringComparison.Ordinal))
            .Select(line => Decided(line[(ScenarioCatalog.AnswerChild.Length + 1)..], decision, fields))
            .FirstOrDefault(answer => answer.IsSome);
    }

    public static Option<EarlyResult> Early(string result, Option<string> decision) =>
        Parsed(result).Match(Early(decision), () => Option<EarlyResult>.None);

    private static Func<JsonNode, Option<EarlyResult>> Early(Option<string> decision) =>
        parsed => parsed is JsonObject answered && answered[ScenarioCatalog.WaitChild] is JsonObject wait
            ? new EarlyResult(
                answered[ScenarioCatalog.AnswerChild] is JsonObject answer ? decision.Bind(chosen => Set(answer, chosen, Array(answered["fields"]))) : Option<string>.None,
                wait.ToJsonString())
            : Option<EarlyResult>.None;

    private static Option<string> Decided(string example, string decision, Option<JsonArray> fields) =>
        Parsed(example).Match(parsed => parsed is JsonObject input ? Set(input, decision, fields) : Option<string>.None, () => Option<string>.None);

    private static Option<string> Set(JsonObject input, string decision, Option<JsonArray> fields)
    {
        input["decision"] = decision;

        foreach (var asked in decision == "allow" ? fields.Match<JsonArray[]>(found => [found], () => []) : [])
        {
            input["fields"] = new JsonArray([.. asked.OfType<JsonObject>().Select(Chosen)]);
        }

        return input.ToJsonString();
    }

    private static Option<JsonArray> Array(JsonNode? node) => node is JsonArray array ? array : Option<JsonArray>.None;

    private static JsonNode Chosen(JsonObject field)
    {
        var answer = new JsonObject { ["id"] = field["id"]?.GetValue<string>() ?? string.Empty };
        var first = (field["options"] as JsonArray)?.FirstOrDefault()?.GetValue<string>();

        if (first is not null)
        {
            answer["chosen"] = new JsonArray(first);
        }
        else if (field["kind"]?.GetValue<string>() == "Confirmation")
        {
            answer["confirmed"] = true;
        }
        else
        {
            answer["text"] = "As the orchestrator sees fit.";
        }

        return answer;
    }

    private static Option<JsonNode> Parsed(string text)
    {
        try
        {
            return JsonNode.Parse(text).ToOption();
        }
        catch (JsonException)
        {
            return Option<JsonNode>.None;
        }
    }
}
