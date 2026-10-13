using System.Text;
using System.Text.Json;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Loops;
using Avala.Sdk;

namespace Avala.Autopilot.RepositoryFiles;

internal static class LoopLimitsParser
{
    public const int MaximumBytes = 16 * 1024;

    private const int MostIterations = 10_000;

    private const int MostInARow = 100;

    private static readonly string[] Fields = ["iterations", "failuresInARow", "sameFailure", "nothingChanged", "pauseAtLimit"];

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 2, AllowDuplicateProperties = false };

    public static Result<LoopLimitsDeclaration, AutopilotError> ParseMachine(string text) =>
        Encoding.UTF8.GetByteCount(text) > MaximumBytes ? AutopilotError.TooLarge : Machine(text);

    public static Result<LoopLimitsDeclaration, AutopilotError> ParseJobFile(string text) =>
        Encoding.UTF8.GetByteCount(text) > MaximumBytes ? AutopilotError.TooLarge
        : JsonSections.Read(
            text,
            AutopilotRulesReader.Section,
            AutopilotRulesReader.SectionDepth,
            AutopilotError.Malformed,
            section => section.Match(declared => Limits(declared, AutopilotRulesReader.RuleFields), () => LoopLimitsDeclaration.None));

    public static Result<LoopLimitsDeclaration, AutopilotError> Limits(JsonElement section, IReadOnlyList<string> others)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return AutopilotError.Malformed;
        }

        if (section.EnumerateObject().Any(field => !Fields.Contains(field.Name) && !others.Contains(field.Name)))
        {
            return AutopilotError.UnknownField;
        }

        return Count(section, "iterations", MostIterations)
            .Bind(iterations => Count(section, "failuresInARow", MostInARow)
                .Bind(failures => Count(section, "sameFailure", MostInARow)
                    .Bind(same => Count(section, "nothingChanged", MostInARow)
                        .Bind(nothing => Fraction(section, "pauseAtLimit")
                            .Map(pause => new LoopLimitsDeclaration
                            {
                                Iterations = iterations,
                                FailuresInARow = failures,
                                SameFailure = same,
                                NothingChanged = nothing,
                                PauseAtLimit = pause,
                            })))));
    }

    private static Result<LoopLimitsDeclaration, AutopilotError> Machine(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Limits(document.RootElement, []);
        }
        catch (JsonException)
        {
            return AutopilotError.Malformed;
        }
    }

    private static Result<Option<int>, AutopilotError> Count(JsonElement section, string field, int most) =>
        !section.TryGetProperty(field, out var value) ? Option<int>.None
        : value.ValueKind != JsonValueKind.Number ? AutopilotError.Malformed
        : value.TryGetInt32(out var count) && count is > 0 && count <= most ? Option<int>.Some(count)
        : AutopilotError.InvalidLimits;

    private static Result<Option<double>, AutopilotError> Fraction(JsonElement section, string field) =>
        !section.TryGetProperty(field, out var value) ? Option<double>.None
        : value.ValueKind != JsonValueKind.Number ? AutopilotError.Malformed
        : value.GetDouble() is > 0 and <= 1 ? Option<double>.Some(value.GetDouble())
        : AutopilotError.InvalidLimits;
}
