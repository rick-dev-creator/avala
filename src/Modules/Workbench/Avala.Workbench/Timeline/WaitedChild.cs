using System.Text.Json;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Timeline;

internal static class WaitedChild
{
    public static Option<JobId> Of(ToolEntry call) =>
        call.Title == WaitChildInput.Tool ? call.Input.Bind(Waited) : Option<JobId>.None;

    private static Option<JobId> Waited(string input)
    {
        try
        {
            return JsonSerializer.Deserialize<WaitChildInput>(input, JsonSerializerOptions.Web) is { Child: { } child } && Guid.TryParse(child, out var job)
                ? new JobId(job)
                : Option<JobId>.None;
        }
        catch (JsonException)
        {
            return Option<JobId>.None;
        }
    }
}
