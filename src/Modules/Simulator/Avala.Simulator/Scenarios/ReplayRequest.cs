using Avala.Sdk;

namespace Avala.Simulator.Scenarios;

internal sealed record ReplayRequest(string Recording, bool AsRecorded)
{
    public const string CompressedTag = "[replay:";

    public const string TimedTag = "[replay as recorded:";

    public string Scenario => $"{(AsRecorded ? TimedTag : CompressedTag)[1..]}{Recording}";

    public bool NamesAFile =>
        Recording.Length > 0
        && Recording[0] != '.'
        && Recording.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    public static Option<ReplayRequest> Parse(string scenario) =>
        scenario.StartsWith(TimedTag[1..], StringComparison.Ordinal) ? new ReplayRequest(scenario[(TimedTag.Length - 1)..], AsRecorded: true)
        : scenario.StartsWith(CompressedTag[1..], StringComparison.Ordinal) ? new ReplayRequest(scenario[(CompressedTag.Length - 1)..], AsRecorded: false)
        : Option<ReplayRequest>.None;
}
