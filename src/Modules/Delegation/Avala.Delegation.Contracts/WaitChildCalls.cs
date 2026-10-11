using System.Text.Json;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Contracts;

public static class WaitChildCalls
{
    public const string Tool = "wait_child";

    public static Option<JobId> Child(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.EnumerateObject().All(field => field.Name == "child")
                && root.TryGetProperty("child", out var child)
                && child.ValueKind == JsonValueKind.String
                && Guid.TryParse(child.GetString(), out var job)
                    ? new JobId(job)
                    : Option<JobId>.None;
        }
        catch (JsonException)
        {
            return Option<JobId>.None;
        }
    }
}
