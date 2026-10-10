using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed record EarlyResult(Option<string> Answer, string Wait);

internal static class ChildRequests
{
    public static Option<string> Answer(string message, string decision) =>
        message.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(ScenarioCatalog.AnswerChild + " ", StringComparison.Ordinal))
            .Select(line => Decided(line[(ScenarioCatalog.AnswerChild.Length + 1)..], decision))
            .FirstOrDefault(answer => answer.IsSome);

    public static Option<EarlyResult> Early(string result, Option<string> decision)
    {
        try
        {
            return JsonNode.Parse(result) is JsonObject answered && answered[ScenarioCatalog.WaitChild] is JsonObject wait
                ? new EarlyResult(
                    answered[ScenarioCatalog.AnswerChild] is JsonObject answer ? decision.Bind(chosen => Set(answer, chosen)) : Option<string>.None,
                    wait.ToJsonString())
                : Option<EarlyResult>.None;
        }
        catch (JsonException)
        {
            return Option<EarlyResult>.None;
        }
    }

    private static Option<string> Decided(string example, string decision)
    {
        try
        {
            return JsonNode.Parse(example) is JsonObject input ? Set(input, decision) : Option<string>.None;
        }
        catch (JsonException)
        {
            return Option<string>.None;
        }
    }

    private static Option<string> Set(JsonObject input, string decision)
    {
        input["decision"] = decision;

        return input.ToJsonString();
    }
}
